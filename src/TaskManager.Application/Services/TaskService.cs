using TaskManager.Application.Interfaces;
using TaskManager.Application.Models;
using TaskManager.Domain.Entities;

namespace TaskManager.Application.Services;

public sealed class TaskService(
    IBoardRepository boardRepository,
    ITaskRepository taskRepository) : ITaskService
{
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

        board.UpdatedAtUtc = now;
        board.MarkActivity(now);
        await taskRepository.AddAsync(board, task, cancellationToken);
        return task.Id;
    }
}
