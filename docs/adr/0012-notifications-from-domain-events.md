# ADR 0012: Notifications from domain events

- Status: accepted
- Date: 2026-11-04

## Context

The original's inbox is filled by Spring listeners: content, social and community publish an event
(`PostLikedEvent`, `FriendRequestSentEvent`, …), and the notification module reacts with
`@TransactionalEventListener(AFTER_COMMIT)` and a transaction of its own (`REQUIRES_NEW`). The like
is committed before the notification is written, and a notification that fails to be written
neither undoes the like nor turns the request into an error.

This repository has one mechanism for both of the original's listener kinds (ADR 0006): handlers run
in the publisher's scope, synchronously, and their exceptions reach the publisher. When they run is
decided by where the publisher publishes.

## Decisions

**The events are published after the publisher's save.** `PostLiked`, `PostCommented`, `PostShared`,
`FriendRequestSent`, `FriendRequestAccepted`, `CommunityMemberJoined` and
`CommunityMemberRoleChanged` live in their modules' `Contracts` and are published only for a real
change: a new like, not a repeated `PUT`; a new membership; a role that actually changed. Each one
states a committed fact.

**Recording a notification never fails the request.** `NotificationService.RecordAsync` catches what
the write throws, logs it with the type and the recipient's id, and clears its change tracker, so the
next notification of the same request does not try to save the failed one again. That keeps the
original's behaviour (`REQUIRES_NEW` plus an after-commit listener): the inbox is a reaction, and
losing one entry degrades the inbox, not the like. `NotificationFailureTests` renames the table away
in a temporary database and checks that the like still returns 204 and is counted. Cancellation is
not caught: an aborted request stops where it is.

**The rules stay in the inbox, not at the publishers.** Own activity, once-per-resource
deduplication (likes, shares, memberships) and collapsing an unread friend request are all decided
in `RecordAsync`. The publishers stay faithful to the facts (analytics will count every like); only
the inbox knows what it has already shown (the original's ADR 0035).

**The entity is `UserNotification`.** Inside `Songly.Notification.*` the simple name `Notification`
resolves to the module's namespace before any `using`, so the original's class name does not compile
without qualifying it everywhere. The table is still `notifications`.

## Consequences

- A notification can be lost when its write fails, and nobody retries it. The original accepted the
  same loss.
- The handler runs inside the HTTP request, so the request waits for the inbox write. For one
  recipient that is one or two short queries; the moderation part fans out to every moderator of a
  community, which stays a handful of rows.
- Moderation decisions follow the same path: a new ban, an appeal and its answers, the second
  instance's decision, a removed post or comment, a post entering the queue, a dismissed report. The
  gates that keep them from repeating ("only a new ban", "only when the post had no pending report",
  "only the click that changed the report") sit at the publishers, so every later channel — e-mail,
  push — inherits them. Notifications to a community's moderators ask `ICommunityMembership` for them
  at the moment of the event; failing to find them is swallowed like a failed write.
