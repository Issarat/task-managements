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

        // SaveChanges commits the task and the board activity update together.
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task AddAsync(Board board, TaskItem task,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(cancellationToken);

        var lastOrder = await dbContext.TaskItems
            .Where(item => item.BoardId == board.Id
                && item.BoardColumnId == task.BoardColumnId)
            .MaxAsync(item => (decimal?)item.SortOrder, cancellationToken) ?? 0m;

        task.SortOrder = lastOrder + 1m;
        await dbContext.TaskItems.AddAsync(task, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
