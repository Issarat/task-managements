using System.Data;
using Microsoft.EntityFrameworkCore;
using TaskManager.Application.Interfaces;
using TaskManager.Application.Models;
using TaskManager.Infrastructure.Persistence;

namespace TaskManager.Infrastructure.Services;

internal sealed class ExpiredDataCleanupService(ApplicationDbContext dbContext)
    : IExpiredDataCleanupService
{
    public async Task<ExpiredDataCleanupResult> CleanupAsync(DateTime utcNow, int batchSize,
        CancellationToken cancellationToken = default)
    {
        if (utcNow.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Cleanup cutoff must be UTC.", nameof(utcNow));
        if (batchSize is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(batchSize));

        // Keep the selected batch stable and commit all related deletions together.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var expiredBoardIds = await dbContext.Boards.AsNoTracking()
            .Where(board => board.ExpiresAtUtc <= utcNow)
            .OrderBy(board => board.ExpiresAtUtc).ThenBy(board => board.Id)
            .Select(board => board.Id).Take(batchSize).ToArrayAsync(cancellationToken);

        var tasks = 0;
        var participants = 0;
        var columns = 0;
        var boards = 0;
        if (expiredBoardIds.Length > 0)
        {
            // Task FKs refer to both participants and columns: delete children first.
            tasks = await dbContext.TaskItems.Where(task => expiredBoardIds.Contains(task.BoardId))
                .ExecuteDeleteAsync(cancellationToken);
            participants = await dbContext.BoardParticipants
                .Where(participant => expiredBoardIds.Contains(participant.BoardId))
                .ExecuteDeleteAsync(cancellationToken);
            columns = await dbContext.BoardColumns.Where(column => expiredBoardIds.Contains(column.BoardId))
                .ExecuteDeleteAsync(cancellationToken);
            boards = await dbContext.Boards.Where(board => expiredBoardIds.Contains(board.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        // An expired guest may still be the author/assignee of tasks on a live board.
        // Preserve that identity until its board memberships are no longer needed.
        var expiredGuestIds = await dbContext.AnonymousUsers.AsNoTracking()
            .Where(guest => guest.ExpiresAtUtc <= utcNow && !guest.BoardParticipants.Any())
            .OrderBy(guest => guest.ExpiresAtUtc).ThenBy(guest => guest.Id)
            .Select(guest => guest.Id).Take(batchSize).ToArrayAsync(cancellationToken);
        var guests = expiredGuestIds.Length == 0 ? 0
            : await dbContext.AnonymousUsers.Where(guest => expiredGuestIds.Contains(guest.Id))
                .ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new ExpiredDataCleanupResult(boards, tasks, participants, columns, guests);
    }
}
