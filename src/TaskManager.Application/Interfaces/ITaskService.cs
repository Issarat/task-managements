using TaskManager.Application.Models;

namespace TaskManager.Application.Interfaces;

public interface ITaskService
{
    Task<Guid?> CreateAsync(string publicId, Guid anonymousUserId,
        CreateTaskRequestModel request,
        CancellationToken cancellationToken = default);
}
