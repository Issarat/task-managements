# task-managements

## Real-time board updates

`BoardHub` checks the existing guest cookie and active board membership before
joining `board:{publicId}`. Controllers send `BoardChanged` only after successful
task create/update/delete/move or board update/reset. A notification failure is
logged without undoing a committed save. No database migration is needed.

The browser fetches the authenticated `/boards/{publicId}/snapshot` on changes
and reconnects, replacing only cards, heading, members and navigation. Open
dialogs, unsaved values and their original row versions remain unchanged.
Refresh waits while dragging or submitting a move. Snapshot refuses expired
sessions and never creates a guest or seeds a board.

SignalR JavaScript client 10.0.0 is vendored in
`src/TaskManager.WebUI/wwwroot/lib/microsoft/signalr/` with the upstream MIT
license, so the deployed browser does not depend on a CDN.

Regression checks (fake data, no production database access):

```text
node tests/board-realtime.checks.cjs
dotnet run --project tests/TaskManager.Realtime.Checks
```

For a manual smoke test, open the same public board URL in two browsers and
create, edit, delete or move a task. The other browser should update without
F5. Keep an edit dialog open there to confirm its unsaved inputs are preserved.

## Online board members

The header avatars now reflect live SignalR connections, not historical
`BoardParticipants` rows. The server derives the identity from the guest cookie,
deduplicates multiple tabs for the same guest, and broadcasts an updated roster
when a connection joins or disconnects. Up to four avatars are shown, with `+N`
and a tooltip containing the remaining names. Members used by task assignee
fields still come from the database, including people currently offline.

Guest/board expiry removes connections from presence and their SignalR group
within five seconds and tells the browser to stop its connection. An abrupt
network loss is reflected once SignalR detects the disconnect, not necessarily
instantly. The browser clears unknown online state during reconnect and never
replaces online avatars with DB membership markup during a task refresh.

Presence is in memory and supports a single application instance. Server
restart clears it and reconnecting clients rebuild it. Multiple application
instances would need shared presence and a SignalR scale-out setup. No migration
or online-status DB column is required.

## Active task editing

Opening the edit dialog invokes `StartEditingTask` after joining the board.
The hub checks guest membership and that the task belongs to that board; names
and colors come from the server, not client arguments. Each connection tracks
one edited task. Multiple tabs for the same guest are deduplicated per task.

`BoardPresenceChanged` includes editing tasks alongside the online roster. Cards
show the first editor's color as a border and a tooltip with all editor names.
This is an advisory indicator, not a lock: anyone can still edit, and existing
row-version conflict checks remain in effect. Close, Escape or backdrop dismissal
sends `StopEditingTask`. Disconnect and expiry remove that connection's state;
another editing tab keeps the indicator alive. Reconnect reannounces an open
dialog, and task refreshes reapply indicators without replacing draft fields.

Manual check: use two guests in separate browsers, open the same task and hover
its card in the other browser. Confirm both names appear, then close one dialog
and confirm only the other name remains. Close the final dialog and check that
the border disappears. Repeat with two tabs for one guest and a reconnect. Like
online presence, abrupt network loss clears once SignalR detects it.

## Row-version concurrency

First-time board membership is safe across simultaneous tabs. The unique
board/guest index remains the final authority. If another request wins the
INSERT, the repository handles only a duplicate-key failure on its attempted
participant, detaches that failed insert, and confirms the exact membership
exists. Other SQL failures are not treated as successful joins. Board reads
reload when their initial snapshot did not yet contain the joining guest, so
both requests return the same persisted participant. No migration is required.

The SQL regression runner synchronizes two requests after both existence checks
but before their INSERTs, covering the actual race rather than relying on timing.

Task mutations compare only the submitted task's SQL Server `RowVersion`.
Board activity is updated separately inside the same transaction, without
comparing a previously loaded board version. The board write lock is acquired
first for create/update/move/delete/reset, keeping append ordering consistent.
Editing different tasks concurrently succeeds; a stale update/move/delete of
the same task fails without overwriting it or committing activity changes.

Owner board-edit and reset forms submit the eight-byte board version they
originally displayed. Reset checks that version before deleting any tasks,
inside one transaction. Missing, malformed or stale versions are rejected.
Board edit conflicts preserve the draft and its old version, with a reload
link; they never silently retry with a newer token. SignalR refreshes closed
board forms' values and versions together, but leaves open forms untouched.

Board `RowVersion` still covers the whole row, including task activity. A task
save while an owner edit/reset dialog is open therefore invalidates that board
confirmation; reload/reopen and review the latest data before submitting again.
No schema migration is required.

Real SQL Server regression checks require an explicit opt-in:

```text
dotnet run --project tests/TaskManager.Concurrency.Checks -c Release -- --sql-local
```

This uses Windows integrated authentication on `.\SQLEXPRESS`, creates a unique
`TaskManager_RowVersionChecks_<random>` database, and deletes that exact database
in `finally`. It never uses the application's connection string or the demo
`TaskManager` database. The SQL user needs create/drop-database permissions.
Checks include synchronized independent-task and same-task writes, parallel
creation ordering, stale update/delete/move, board editing, and atomic reset.

## Expired demo data cleanup

The app runs a cleanup once on startup and every 5 minutes while it is running.
Each run deletes at most 100 expired boards (including their tasks, participants
and columns) and 100 expired guests without board memberships. Boards are selected
using `ExpiresAtUtc <= current UTC time`; valid boards are never reset or reseeded.
An expired guest referenced by a surviving board is retained to preserve task
authors and assignees until that board is removed. Each batch runs in a transaction.
Failures are logged and retried on the next interval.

Configure `ExpiredDataCleanup` in `src/TaskManager.WebUI/appsettings.json`:

```json
{
  "ExpiredDataCleanup": {
    "Enabled": true,
    "IntervalMinutes": 5,
    "BatchSize": 100
  }
}
```

`IntervalMinutes` must be 1–1440 and `BatchSize` must be 1–1000.
If the host stops or sleeps, cleanup resumes when the app starts again. Session
and board expiration checks still reject expired access before physical deletion.
No additional database migration is required.
