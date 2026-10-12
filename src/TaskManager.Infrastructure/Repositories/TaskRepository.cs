using Microsoft.EntityFrameworkCore;
using TaskManager.Application.Interfaces;
using TaskManager.Domain.Entities;
using TaskManager.Infrastructure.Persistence;

namespace TaskManager.Infrastructure.Repositories;

internal sealed class TaskRepository(ApplicationDbContext dbContext) : ITaskRepository
{
    public Task<TaskItem?> GetAsync(Guid boardId, Guid taskId,
        CancellationToken cancellationToken = default) =>
        dbContext.TaskItems.SingleOrDefaultAsync(
            task => task.BoardId == boardId && task.Id == taskId,
            cancellationToken);

    public async Task SaveAsync(TaskItem task, byte[] rowVersion, bool delete,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        // Acquire the board write lock first (same order as create/reset), but
        // never compare its old RowVersion for an independent task operation.
        await UpdateBoardActivityAsync(task.BoardId, cancellationToken);
        dbContext.Entry(task).Property(item => item.RowVersion).OriginalValue = rowVersion;
        if (delete)
        {
            dbContext.TaskItems.Remove(task);
        }
        else if (dbContext.Entry(task).Property(item => item.BoardColumnId).OriginalValue
            != task.BoardColumnId)
        {
            // A moved task goes to the end of the destination column.
            var lastOrder = await dbContext.TaskItems
                .Where(item => item.BoardId == task.BoardId
                    && item.BoardColumnId == task.BoardColumnId && item.Id != task.Id)
                .MaxAsync(item => (decimal?)item.SortOrder, cancellationToken) ?? 0m;
            task.SortOrder = lastOrder + 1m;
        }

        if (!delete)
            dbContext.Entry(task).Property(item => item.UpdatedAtUtc).IsModified = true;

        // Only the task is modified in the change tracker. A stale task rolls
        // back its activity update too; never refresh/rebase its submitted version.
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task AddAsync(Board board, TaskItem task,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(cancellationToken);

        await UpdateBoardActivityAsync(board.Id, cancellationToken);

        var lastOrder = await dbContext.TaskItems
            .Where(item => item.BoardId == board.Id
                && item.BoardColumnId == task.BoardColumnId)
            .MaxAsync(item => (decimal?)item.SortOrder, cancellationToken) ?? 0m;

        task.SortOrder = lastOrder + 1m;
        await dbContext.TaskItems.AddAsync(task, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task UpdateBoardActivityAsync(Guid boardId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        // SQL-only bookkeeping: leave the tracked Board unchanged, so
        // SaveChanges cannot overwrite its metadata or require its old version.
        var updated = await dbContext.Boards
            .Where(board => board.Id == boardId && board.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(board => board.LastActivityAtUtc,
                    board => board.LastActivityAtUtc > now ? board.LastActivityAtUtc : now)
                .SetProperty(board => board.UpdatedAtUtc,
                    board => board.UpdatedAtUtc > now ? board.UpdatedAtUtc : now), cancellationToken);
        if (updated != 1)
            throw new DbUpdateConcurrencyException("The board no longer exists or has expired.");
    }
}
