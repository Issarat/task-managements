using Microsoft.EntityFrameworkCore;
using TaskManager.Application.Interfaces;
using TaskManager.Domain.Entities;
using TaskManager.Infrastructure.Persistence;

namespace TaskManager.Infrastructure.Repositories;

internal sealed class TaskRepository(ApplicationDbContext dbContext) : ITaskRepository
{
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
