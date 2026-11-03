# ADR 0009: Pagination

- Status: accepted
- Date: 2026-11-03

## Context

Every list in the API is paged with `?page=&size=` (original Claude.md §6). The original used
Spring Data's `Pageable`, whose behaviour the mobile app was written against, and serialized
pages as Spring's `PagedModel`: `{ "content": [...], "page": { "size", "number", "totalElements",
"totalPages" } }`. ASP.NET Core has no equivalent.

## Decisions

**`PageQuery` reads the query string the way Spring did, and rejects nothing.** A missing,
malformed or negative `page` is the first page. A missing, malformed or non-positive `size` is the
default (20, the default of every list in the original). A `size` above 100 is cut down to 100. A
client asking for 2000 rows gets a page, not an error and not 2000 rows. The original lowered
Spring's own ceiling of 2000 to 100 because, with 2000, one request downloaded 2000 profiles from
the user search. `PageQuery` binds itself (`BindAsync`), so an endpoint only declares the
parameter.

**`sort` is not supported.** Spring accepted it on every list and ordered by any property the
client named. No client sends it, and every list has its own fixed order.

**Every paged query has a fixed, unique order.** Without one, Postgres may order the rows of one
page differently on the next request, and a row can appear on two pages or on none.
`ToPageAsync` takes an ordered query for that reason. The genre list is ordered by id, which
reproduces what the original's unordered query returned in practice: the order of the seed data.

**Filters belong in the query, never after the page is read.** Filtering afterwards returns short
pages and counts hidden rows in `totalElements` (original Claude.md §9, about blocks). The offset
is a `long`, because a huge page number times the size would wrap around in an `int` and return
some other page.

## Consequences

- A list endpoint takes `PageQuery page`, builds an ordered `IQueryable` with its filters, and
  returns `ToPageAsync(page, map, ct)`.
- Query-string collections follow Spring too: `RequiredGuidSet` accepts both `?ids=a&ids=b` and
  `?ids=a,b`. The mobile app sends the second form, which Minimal APIs do not bind on their own.
