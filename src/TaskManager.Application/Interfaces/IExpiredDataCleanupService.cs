using TaskManager.Application.Models;

namespace TaskManager.Application.Interfaces;

public interface IExpiredDataCleanupService
{
    /// <summary>
    /// Deletes a bounded batch of expired boards and unreferenced expired guests.
    /// Uses a fixed UTC cutoff; active boards and their task authors are preserved.
    /// </summary>
    Task<ExpiredDataCleanupResult> CleanupAsync(DateTime utcNow, int batchSize,
        CancellationToken cancellationToken = default);
}
