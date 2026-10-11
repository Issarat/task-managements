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
