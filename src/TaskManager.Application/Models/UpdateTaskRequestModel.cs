using TaskManager.Domain.Enums;

namespace TaskManager.Application.Models;

public sealed record UpdateTaskRequestModel(
    Guid BoardColumnId,
    string Title,
    string? DescriptionMarkdown,
    TaskPriority Priority,
    Guid? AssignedToParticipantId,
    DateOnly? StartDate,
    DateOnly? DueDate);
