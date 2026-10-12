using TaskManager.Application.Exceptions;
using TaskManager.Application.Interfaces;
using TaskManager.Application.Models;
using TaskManager.Domain.Entities;

namespace TaskManager.Application.Services;

public sealed class TaskService(
    IBoardRepository boardRepository,
    ITaskRepository taskRepository) : ITaskService
{
    public async Task<bool> MoveAsync(string publicId, Guid anonymousUserId,
        Guid taskId, Guid targetColumnId, byte[] rowVersion,
        CancellationToken cancellationToken = default)
    {
        ValidateRowVersion(rowVersion);
        var board = await GetParticipantBoardAsync(publicId, anonymousUserId, cancellationToken);
        if (board is null)
            return false;

        var task = await taskRepository.GetAsync(board.Id, taskId, cancellationToken);
        if (task is null)
            return false;

        ValidateColumnChange(board, task.BoardColumnId, targetColumnId);
        if (task.BoardColumnId == targetColumnId)
            return true;

        task.BoardColumnId = targetColumnId;
        task.UpdatedAtUtc = DateTime.UtcNow;
        await taskRepository.SaveAsync(task, rowVersion, delete: false, cancellationToken);
        return true;
    }

    public async Task<bool> UpdateAsync(string publicId, Guid anonymousUserId,
        Guid taskId, UpdateTaskRequestModel request, byte[] rowVersion,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        ValidateRowVersion(rowVersion);
        var board = await GetParticipantBoardAsync(publicId, anonymousUserId, cancellationToken);
        if (board is null)
            return false;

        var task = await taskRepository.GetAsync(board.Id, taskId, cancellationToken);
        if (task is null)
            return false;

        ValidateAssignee(board, request.AssignedToParticipantId);
        ValidateColumnChange(board, task.BoardColumnId, request.BoardColumnId);

        task.BoardColumnId = request.BoardColumnId;
        task.Title = request.Title.Trim();
        task.DescriptionMarkdown = string.IsNullOrWhiteSpace(request.DescriptionMarkdown)
            ? null : request.DescriptionMarkdown.Trim();
        task.Priority = request.Priority;
        task.AssignedToParticipantId = request.AssignedToParticipantId;
        task.StartDate = request.StartDate;
        task.DueDate = request.DueDate;
        task.UpdatedAtUtc = DateTime.UtcNow;
        await taskRepository.SaveAsync(task, rowVersion, delete: false, cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(string publicId, Guid anonymousUserId,
        Guid taskId, byte[] rowVersion, CancellationToken cancellationToken = default)
    {
        ValidateRowVersion(rowVersion);
        var board = await GetParticipantBoardAsync(publicId, anonymousUserId, cancellationToken);
        if (board is null)
            return false;

        var task = await taskRepository.GetAsync(board.Id, taskId, cancellationToken);
        if (task is null)
            return false;

        await taskRepository.SaveAsync(task, rowVersion, delete: true, cancellationToken);
        return true;
    }

    private async Task<Board?> GetParticipantBoardAsync(string publicId,
        Guid anonymousUserId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicId);
        var board = await boardRepository.GetByPublicIdForUpdateAsync(publicId, cancellationToken);
        if (board is null || board.IsExpired(DateTime.UtcNow))
            return null;

        if (!board.Participants.Any(participant => participant.AnonymousUserId == anonymousUserId))
            throw new UnauthorizedAccessException("Guest is not a board participant.");

        return board;
    }

    private static void ValidateColumnChange(Board board, Guid currentColumnId, Guid targetColumnId)
    {
        var columns = board.Columns.OrderBy(column => column.SortOrder).ToArray();
        var currentIndex = Array.FindIndex(columns, column => column.Id == currentColumnId);
        var targetIndex = Array.FindIndex(columns, column => column.Id == targetColumnId);
        if (currentIndex < 0 || targetIndex < 0)
            throw new ArgumentException("Column does not belong to this board.");

        if (Math.Abs(targetIndex - currentIndex) > 1)
            throw new InvalidTaskColumnChangeException();
    }

    private static void ValidateRowVersion(byte[] rowVersion)
    {
        if (rowVersion is null || rowVersion.Length != 8)
            throw new ArgumentException("A valid task row version is required.", nameof(rowVersion));
    }

    private static void ValidateAssignee(Board board, Guid? participantId)
    {
        if (participantId.HasValue
            && !board.Participants.Any(participant => participant.Id == participantId.Value))
            throw new ArgumentException("Assignee does not belong to this board.");
    }

    private static void ValidateRequest(UpdateTaskRequestModel request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Title);
        if (request.Title.Trim().Length > 60 || request.DescriptionMarkdown?.Length > 20_000
            || !Enum.IsDefined(request.Priority)
            || (request.StartDate.HasValue && request.DueDate.HasValue
                && request.StartDate.Value > request.DueDate.Value))
            throw new ArgumentException("Invalid task details.", nameof(request));
    }

    public async Task<Guid?> CreateAsync(
        string publicId,
        Guid anonymousUserId,
        CreateTaskRequestModel request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicId);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Title);

        var title = request.Title.Trim();
        if (title.Length > 60 || request.DescriptionMarkdown?.Length > 20_000
            || !Enum.IsDefined(request.Priority)
            || (request.StartDate.HasValue && request.DueDate.HasValue
                && request.StartDate.Value > request.DueDate.Value))
        {
            throw new ArgumentException("Invalid task details.", nameof(request));
        }

        var board = await boardRepository.GetByPublicIdForUpdateAsync(
            publicId, cancellationToken);
        var now = DateTime.UtcNow;
        if (board is null || board.IsExpired(now))
        {
            return null;
        }

        var creator = board.Participants.SingleOrDefault(
            participant => participant.AnonymousUserId == anonymousUserId);
        if (creator is null)
        {
            throw new UnauthorizedAccessException("Guest is not a board participant.");
        }

        var backlog = board.Columns.SingleOrDefault(column => column.ColumnKey == "backlog")
            ?? throw new ArgumentException("Board must have a Backlog column.", nameof(request));

        if (request.AssignedToParticipantId.HasValue
                && !board.Participants.Any(participant =>
                    participant.Id == request.AssignedToParticipantId.Value))
        {
            throw new ArgumentException("Assignee does not belong to this board.",
                nameof(request));
        }

        var task = new TaskItem
        {
            BoardId = board.Id,
            BoardColumnId = backlog.Id,
            Title = title,
            DescriptionMarkdown = string.IsNullOrWhiteSpace(request.DescriptionMarkdown)
                ? null : request.DescriptionMarkdown.Trim(),
            Priority = request.Priority,
            AssignedToParticipantId = request.AssignedToParticipantId,
            CreatedByParticipantId = creator.Id,
            StartDate = request.StartDate,
            DueDate = request.DueDate,
            CreatedAtUtc = now
        };

        await taskRepository.AddAsync(board, task, cancellationToken);
        return task.Id;
    }
}
