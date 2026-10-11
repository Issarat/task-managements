using TaskManager.Domain.Enums;

namespace TaskManager.Application.Models;

public sealed record BoardResponseModel(
    BoardModel Board,
    IReadOnlyList<BoardParticipantModel> Participants,
    IReadOnlyList<BoardColumnModel> Columns,
    IReadOnlyList<BoardTaskModel> Tasks);

public sealed record BoardNavigationModel(
    Guid BoardId,
    string PublicId,
    string Name);

public sealed record BoardModel(
    Guid BoardId,
    string PublicId,
    string Name,
    string? Description,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc);

public sealed record BoardColumnModel(
    Guid BoardColumnId,
    string Name,
    string ColumnKey,
    int SortOrder);

public sealed record BoardParticipantModel(
    Guid BoardParticipantId,
    Guid AnonymousUserId,
    string DisplayName,
    string AvatarColor
);

public sealed record BoardTaskModel(
    Guid TaskItemId,
    Guid BoardColumnId,
    string Title,
    string? DescriptionMarkdown,
    TaskPriority Priority,
    decimal SortOrder,
    Guid? AssignedToParticipantId,
    Guid CreatedByParticipantId,
    DateOnly? StartDate,
    DateOnly? DueDate,
    byte[] RowVersion);

public sealed record CreateBoardRequestModel(
    Guid AnonymousUserId,
    string Name,
    string? Description,
    DateTime ExpiresAtUtc,
    bool IncludeSampleData = false);

public sealed record CreatedBoardModel(
    BoardModel Board,
    string OwnerToken);

public sealed record UpdateBoardRequestModel(
    string Name,
    string? Description);
