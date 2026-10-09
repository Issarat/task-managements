using System.Security.Cryptography;
using System.Text;
using TaskManager.Application.Interfaces;
using TaskManager.Application.Models;
using TaskManager.Domain.Entities;

namespace TaskManager.Application.Services;

public sealed class BoardService(IBoardRepository boardRepository)
    : IBoardService
{
    private const int MaximumNameLength = 120;
    private const int MaximumDescriptionLength = 500;

    public async Task<CreatedBoardModel> CreateAsync(
        CreateBoardRequestModel request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
    
        if (request.AnonymousUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "Anonymous user ID is required.",
                nameof(request));
        }

        var name = NormalizeName(request.Name);
        var description = NormalizeDescription(request.Description);
        var now = DateTime.UtcNow;

        //เช็ค board life time
        if (request.ExpiresAtUtc <= now)
        {
            throw new ArgumentException(
                "Board expiry must be in the future.",
                nameof(request));
        }

        //add owner board
        var ownerToken = CreateUrlSafeToken(32);
        var ownerParticipant = new BoardParticipant
        {
            AnonymousUserId = request.AnonymousUserId,
            JoinedAtUtc = now,
            LastSeenAtUtc = now
        };

        //set default data
        var columns = CreateDefaultColumns(now);
        var board = new Board
        {
            PublicId = CreateUrlSafeToken(16),
            Name = name,
            Description = description,
            OwnerTokenHash = ComputeHash(ownerToken),
            CreatedAtUtc = now,
            LastActivityAtUtc = now,
            ExpiresAtUtc = request.ExpiresAtUtc,
            Participants =
            [
                ownerParticipant
            ],
            Columns = columns
        };

        if (request.IncludeSampleData)
        {
            board.Tasks.Add(CreateSampleTask(
                columns[0],
                ownerParticipant,
                now));
        }

        await boardRepository.AddAsync(board, cancellationToken);

        return new CreatedBoardModel(
            MapBoard(board),
            ownerToken);
    }

    public async Task<BoardResponseModel?> GetAsync(
        string publicId,
        Guid anonymousUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicId);

        if (anonymousUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "Anonymous user ID is required.",
                nameof(anonymousUserId));
        }

        var board = await boardRepository.GetByPublicIdAsync(
            publicId,
            cancellationToken);

        var now = DateTime.UtcNow;
        if (board is null || board.IsExpired(now))
        {
            return null;
        }

        var participantAdded = await boardRepository.AddParticipantIfMissingAsync(
            board.Id,
            anonymousUserId,
            now,
            cancellationToken);

        if (participantAdded)
        {
            board = await boardRepository.GetByPublicIdAsync(
                publicId,
                cancellationToken);
        }

        return board is null ? null : MapResponse(board);
    }

    public async Task<IReadOnlyList<BoardNavigationModel>> GetListBySessionIdAsync(
        Guid anonymousUserId,
        CancellationToken cancellationToken = default)
    {
        if (anonymousUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "Anonymous user ID is required.",
                nameof(anonymousUserId));
        }

        var boards = await boardRepository.GetListByAnonymousUserIdAsync(
            anonymousUserId,
            cancellationToken);

        var now = DateTime.UtcNow;

        return boards
            .Where(board => !board.IsExpired(now))
            .Select(board => new BoardNavigationModel(
                board.Id,
                board.PublicId,
                board.Name))
            .ToArray();
    }

    public async Task<BoardModel?> UpdateAsync(
        string publicId,
        string ownerToken,
        UpdateBoardRequestModel request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerToken);
        ArgumentNullException.ThrowIfNull(request);

        var board = await boardRepository.GetByPublicIdForUpdateAsync(
            publicId,
            cancellationToken);

        var now = DateTime.UtcNow;
        if (board is null || board.IsExpired(now))
        {
            return null;
        }

        if (!HasMatchingOwnerToken(board.OwnerTokenHash, ownerToken))
        {
            throw new UnauthorizedAccessException(
                "Only the board owner can update this board.");
        }

        board.Name = NormalizeName(request.Name);
        board.Description = NormalizeDescription(request.Description);
        board.UpdatedAtUtc = now;
        board.MarkActivity(now);

        await boardRepository.SaveChangesAsync(cancellationToken);

        return MapBoard(board);
    }

    #region private zone
    private static List<BoardColumn> CreateDefaultColumns(DateTime now)
    {
        return
        [
            new BoardColumn
            {
                Name = "Backlog",
                ColumnKey = "backlog",
                SortOrder = 1,
                CreatedAtUtc = now
            },
            new BoardColumn
            {
                Name = "To do",
                ColumnKey = "todo",
                SortOrder = 2,
                CreatedAtUtc = now
            },
            new BoardColumn
            {
                Name = "In progress",
                ColumnKey = "in-progress",
                SortOrder = 3,
                CreatedAtUtc = now
            },
            new BoardColumn
            {
                Name = "Done",
                ColumnKey = "done",
                SortOrder = 4,
                CreatedAtUtc = now
            }
        ];
    }

    private static TaskItem CreateSampleTask(
        BoardColumn backlogColumn,
        BoardParticipant ownerParticipant,
        DateTime now)
    {
        var startDate = DateOnly.FromDateTime(now);

        return new TaskItem
        {
            BoardColumnId = backlogColumn.Id,
            Title = "Sample Task Demo",
            DescriptionMarkdown = "นี่คือตัวอย่าง Task",
            Priority = TaskManager.Domain.Enums.TaskPriority.High,
            SortOrder = 1m,
            AssignedToParticipantId = ownerParticipant.Id,
            CreatedByParticipantId = ownerParticipant.Id,
            StartDate = startDate,
            DueDate = startDate.AddDays(7),
            CreatedAtUtc = now
        };
    }

    private static BoardResponseModel MapResponse(Board board)
    {
        return new BoardResponseModel(
            MapBoard(board),
            board.Participants
                .OrderBy(participant => participant.JoinedAtUtc)
                .Select(participant => new BoardParticipantModel(
                    participant.Id,
                    participant.AnonymousUserId,
                    participant.AnonymousUser.DisplayName,
                    participant.AnonymousUser.AvatarColor))
                .ToArray(),
            board.Columns
                .OrderBy(column => column.SortOrder)
                .Select(column => new BoardColumnModel(
                    column.Id,
                    column.Name,
                    column.ColumnKey,
                    column.SortOrder))
                .ToArray(),
            board.Tasks
                .OrderBy(task => task.SortOrder)
                .Select(task => new BoardTaskModel(
                    task.Id,
                    task.BoardColumnId,
                    task.Title,
                    task.DescriptionMarkdown,
                    task.Priority,
                    task.SortOrder,
                    task.AssignedToParticipantId,
                    task.CreatedByParticipantId,
                    task.StartDate,
                    task.DueDate))
                .ToArray());
    }

    private static BoardModel MapBoard(Board board)
    {
        return new BoardModel(
            board.Id,
            board.PublicId,
            board.Name,
            board.Description,
            board.CreatedAtUtc,
            board.ExpiresAtUtc);
    }

    private static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var normalized = name.Trim();
        if (normalized.Length > MaximumNameLength)
        {
            throw new ArgumentException(
                $"Board name cannot exceed {MaximumNameLength} characters.",
                nameof(name));
        }

        return normalized;
    }

    private static string? NormalizeDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var normalized = description.Trim();
        if (normalized.Length > MaximumDescriptionLength)
        {
            throw new ArgumentException(
                $"Board description cannot exceed {MaximumDescriptionLength} characters.",
                nameof(description));
        }

        return normalized;
    }

    private static bool HasMatchingOwnerToken(
        string expectedHash,
        string ownerToken)
    {
        var expectedBytes = Convert.FromHexString(expectedHash);
        var actualBytes = Convert.FromHexString(ComputeHash(ownerToken));

        return CryptographicOperations.FixedTimeEquals(
            expectedBytes,
            actualBytes);
    }

    private static string CreateUrlSafeToken(int byteCount)
    {
        return Convert
            .ToBase64String(RandomNumberGenerator.GetBytes(byteCount))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string ComputeHash(string value)
    {
        var inputBytes = Encoding.UTF8.GetBytes(value);
        return Convert
            .ToHexString(SHA256.HashData(inputBytes))
            .ToLowerInvariant();
    }
    #endregion
}
