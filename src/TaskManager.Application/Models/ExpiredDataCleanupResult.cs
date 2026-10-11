namespace TaskManager.Application.Models;

public sealed record ExpiredDataCleanupResult(
    int DeletedBoards,
    int DeletedTasks,
    int DeletedParticipants,
    int DeletedColumns,
    int DeletedGuests)
{
    public int TotalDeleted => DeletedBoards + DeletedTasks + DeletedParticipants
        + DeletedColumns + DeletedGuests;
}
