using System;
using Microsoft.EntityFrameworkCore;
using TaskManager.Application.Interfaces;
using TaskManager.Domain.Entities;
using TaskManager.Infrastructure.Persistence;

namespace TaskManager.Infrastructure.Repositories;

internal sealed class BoardRepository(ApplicationDbContext dbContext)
    : IBoardRepository
{

    public async Task ResetTasksAsync(
        Board board,
        byte[] rowVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(board);

        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(cancellationToken);

        // Compare the version the owner confirmed BEFORE deleting any tasks.
        // Lock order matches TaskRepository: Board first, TaskItems second.
        await SaveAsync(board, rowVersion, cancellationToken);

        await dbContext.TaskItems
            .Where(task => task.BoardId == board.Id)
            .ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task AddAsync(
        Board board,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(board);

        await dbContext.Boards.AddAsync(board, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<Board?> GetByPublicIdAsync(
        string publicId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicId);

        return dbContext.Boards
            .AsNoTracking()
            .AsSplitQuery()
            .Include(board => board.Participants)
                .ThenInclude(participant => participant.AnonymousUser)
            .Include(board => board.Columns)
            .Include(board => board.Tasks)
            .SingleOrDefaultAsync(
                board => board.PublicId == publicId,
                cancellationToken);
    }

    public Task<Board?> GetByPublicIdForUpdateAsync(
        string publicId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicId);

        return dbContext.Boards
            .Include(board => board.Columns)
            .Include(board => board.Participants)
            .AsSplitQuery()
            .SingleOrDefaultAsync(
            board => board.PublicId == publicId,
            cancellationToken);
    }

    public async Task<IReadOnlyList<Board>> GetListByAnonymousUserIdAsync(
        Guid anonymousUserId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Boards
            .AsNoTracking()
            .Where(board => board.Participants.Any(
                participant => participant.AnonymousUserId == anonymousUserId))
            .OrderByDescending(board => board.LastActivityAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> AddParticipantIfMissingAsync(
        Guid boardId,
        Guid anonymousUserId,
        DateTime joinedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var exists = await dbContext.BoardParticipants.AnyAsync(
            participant => participant.BoardId == boardId
                && participant.AnonymousUserId == anonymousUserId,
            cancellationToken);

        if (exists)
        {
            return false;
        }

        await dbContext.BoardParticipants.AddAsync(
            new BoardParticipant
            {
                BoardId = boardId,
                AnonymousUserId = anonymousUserId,
                JoinedAtUtc = joinedAtUtc,
                LastSeenAtUtc = joinedAtUtc
            },
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public Task SaveAsync(Board board, byte[] rowVersion,
        CancellationToken cancellationToken = default)
    {
        dbContext.Entry(board).Property(item => item.RowVersion).OriginalValue = rowVersion;
        // Always issue the conditional UPDATE, even when submitted values match.
        // Reset must never skip its concurrency check because metadata is unchanged.
        dbContext.Entry(board).Property(item => item.UpdatedAtUtc).IsModified = true;
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
