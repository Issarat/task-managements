using TaskManager.Application.Models;

namespace TaskManager.Application.Interfaces;

public interface ITaskService
{
    Task<bool> MoveAsync(string publicId, Guid anonymousUserId, Guid taskId,
        Guid targetColumnId, byte[] rowVersion,
        CancellationToken cancellationToken = default);

    Task<bool> UpdateAsync(string publicId, Guid anonymousUserId, Guid taskId,
        UpdateTaskRequestModel request, byte[] rowVersion,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string publicId, Guid anonymousUserId, Guid taskId,
        byte[] rowVersion, CancellationToken cancellationToken = default);

    Task<Guid?> CreateAsync(string publicId, Guid anonymousUserId,
        CreateTaskRequestModel request,
        CancellationToken cancellationToken = default);
}
