# task-managements

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
