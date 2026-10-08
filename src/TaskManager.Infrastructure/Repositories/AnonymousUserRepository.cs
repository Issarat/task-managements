using Microsoft.EntityFrameworkCore;
using TaskManager.Application.Interfaces;
using TaskManager.Domain.Entities;
using TaskManager.Infrastructure.Persistence;

namespace TaskManager.Infrastructure.Repositories;

internal sealed class AnonymousUserRepository(ApplicationDbContext dbContext)
    : IAnonymousUserRepository
{
    public Task<AnonymousUser?> GetBySessionTokenHashAsync(
        string sessionTokenHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionTokenHash);

        return dbContext.AnonymousUsers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                user => user.SessionTokenHash == sessionTokenHash,
                cancellationToken);
    }

    public async Task AddAsync(
        AnonymousUser anonymousUser,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(anonymousUser);

        await dbContext.AnonymousUsers.AddAsync(
            anonymousUser,
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> UpdateLastSeenAsync(
        Guid anonymousUserId,
        DateTime lastSeenAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (anonymousUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "Anonymous user ID is required.",
                nameof(anonymousUserId));
        }

        var updatedRows = await dbContext.AnonymousUsers
            .Where(user => user.Id == anonymousUserId
                && user.ExpiresAtUtc > lastSeenAtUtc)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    user => user.LastSeenAtUtc,
                    lastSeenAtUtc),
                cancellationToken);

        return updatedRows == 1;
    }
}
