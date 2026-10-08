using TaskManager.Domain.Entities;

namespace TaskManager.Application.Interfaces;

public interface IAnonymousUserRepository
{
    Task<AnonymousUser?> GetBySessionTokenHashAsync(
        string sessionTokenHash,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        AnonymousUser anonymousUser,
        CancellationToken cancellationToken = default);

    Task<bool> UpdateLastSeenAsync(
        Guid anonymousUserId,
        DateTime lastSeenAtUtc,
        CancellationToken cancellationToken = default);
}
