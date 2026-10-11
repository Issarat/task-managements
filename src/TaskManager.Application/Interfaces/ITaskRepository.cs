using TaskManager.Domain.Entities;

namespace TaskManager.Application.Interfaces;

public interface ITaskRepository
{
    Task<TaskItem?> GetAsync(Guid boardId, Guid taskId,
        CancellationToken cancellationToken = default);

    Task SaveAsync(TaskItem task, byte[] rowVersion, bool delete,
        CancellationToken cancellationToken = default);

    Task AddAsync(Board board, TaskItem task,
        CancellationToken cancellationToken = default);
}
