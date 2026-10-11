using TaskManager.Domain.Enums;

namespace TaskManager.Application.Models;

public sealed record CreateTaskRequestModel(
    string Title,
    string? DescriptionMarkdown,
    TaskPriority Priority,
    Guid? AssignedToParticipantId,
    DateOnly? StartDate,
    DateOnly? DueDate);
