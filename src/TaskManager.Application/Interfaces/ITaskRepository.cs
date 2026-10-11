using TaskManager.Domain.Entities;

namespace TaskManager.Application.Interfaces;

public interface ITaskRepository
{
    Task AddAsync(Board board, TaskItem task,
        CancellationToken cancellationToken = default);
}
