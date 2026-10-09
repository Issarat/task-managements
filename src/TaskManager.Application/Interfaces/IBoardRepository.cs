using TaskManager.Domain.Entities;

namespace TaskManager.Application.Interfaces;

public interface IBoardRepository
{
    Task AddAsync(
        Board board,
        CancellationToken cancellationToken = default);

    Task<Board?> GetByPublicIdAsync(
        string publicId,
        CancellationToken cancellationToken = default);

    Task<Board?> GetByPublicIdForUpdateAsync(
        string publicId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Board>> GetListByAnonymousUserIdAsync(
        Guid anonymousUserId,
        CancellationToken cancellationToken = default);

    Task<bool> AddParticipantIfMissingAsync(
        Guid boardId,
        Guid anonymousUserId,
        DateTime joinedAtUtc,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(
        CancellationToken cancellationToken = default);
}
