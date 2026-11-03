# ADR 0010: Object storage, uploads, and work after the commit

- Status: accepted
- Date: 2026-11-03

## Context

Avatars are the first files in the system (original ADR 0010, 0047; post photos follow with the
content module). The original stores them in S3-compatible storage (Cloudflare R2 in production,
MinIO locally). Uploads go through the backend, which recognises the format by its content and
rewrites the file without its metadata. A file nothing points to any more is deleted after the
transaction commits, never before (`@TransactionalEventListener(AFTER_COMMIT)`). Each of these
needs a .NET counterpart, and the last one has none built in.

## Decisions

**`IObjectStorage` in Common, one S3 implementation** (`AWSSDK.S3`) for S3, R2 and MinIO. The
interface knows keys, bytes and content types only. A storage failure is an
`ObjectStorageException`: 502 `STORAGE_ERROR`, not a 500 with the SDK's stack trace. A failed
delete is reported too, not swallowed (original ADR 0049). Two client settings matter:
- Checksums are sent only where an operation requires one. Since 2025 the SDK adds them to every
  request, and S3-compatible stores do not all accept that.
- Uploads are not chunk-encoded, for the same reason.

The public address comes from its own setting (`PublicBaseUrl`), because behind a CDN it is not
the API's address.

**Uploads go through the backend and come out of `SanitizedImage`.** The format is detected from
the magic bytes, and the key's extension comes from the detected type, so the client's file name
and `Content-Type` decide nothing. The container (JPEG segments, PNG and WebP chunks) is rewritten
from an allow-list, ported line by line from the original, without decoding the image. There is
no image library: none is needed for this, and decoding a 5 MB PNG would mean gigabytes of pixels.
Size limits are part of the endpoint:
- 5 MB per file, checked while the form is read (`WithFormOptions`);
- 6 MB per request (`RequestSizeLimit`).

Both answer 413 `FILE_TOO_LARGE`. The form reader reports its own limit as a 400 around an
`InvalidDataException`, so the error handler recognises that case. A test pins it, so that a
framework upgrade changing the message fails the build.

**Work after the commit is `IAfterCommit.Defer`, run by `ModuleTransaction.RunAsync`.**
`ModuleTransaction` became a scoped service:
1. `RunAsync(work)` opens the ambient transaction, runs the work and commits.
2. Only then does it run what the participants deferred.
3. A rollback discards the deferred work.

A module that must delete a file for rows it deleted inside the transaction defers the deletion,
and it happens only once the database confirms that nothing points to the file. One instance per
DI scope means every handler of a request defers into the transaction that request runs.
Deferring when no transaction is running throws, because nothing would ever run that work. Two
other options were considered:
- `Transaction.TransactionCompleted`, the System.Transactions callback. It is synchronous, so the
  deletion would block inside `Dispose` or become fire-and-forget.
- A second, after-commit event from identity. It would have to carry state, such as the avatar key,
  that only the deleted rows had.

Deferred work that fails is logged and does not fail the request. The transaction has committed,
and the response must not say otherwise.

**Outside account deletion, "after" is statement order** (ADR 0005). Changing an avatar uploads the
new file before the save and deletes the old one after it. Either mistake then leaves an orphaned
file, never a row pointing to nothing.

## Consequences

- The integration tests run MinIO in a container (`cgr.dev/chainguard/minio`, as in the original
  and in docker-compose), with a bucket anyone can read from. They fetch uploaded files
  anonymously, as clients do.
- Test images are built without an imaging library: a PNG assembled byte by byte and a JPEG
  stored as a constant. Where the original decoded the result to prove it is still an image, the
  tests compare it byte for byte with the file it must be, which is a stronger claim.
- Later modules with files (post photos) or other state outside the database use the same two
  tools: `IObjectStorage`, and `IAfterCommit.Defer` from their account-deletion handler.
