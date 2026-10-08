using TaskManager.Domain.Enums;

namespace TaskManager.Domain.Entities;

public sealed class TaskItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BoardId { get; set; }

    public Board Board { get; set; } = null!;

    public Guid BoardColumnId { get; set; }

    public BoardColumn BoardColumn { get; set; } = null!;

    public string Title { get; set; } = string.Empty;

    public string? DescriptionMarkdown { get; set; }

    public TaskPriority Priority { get; set; } = TaskPriority.Medium;

    public decimal SortOrder { get; set; }

    public Guid? AssignedToParticipantId { get; set; }

    public BoardParticipant? AssignedToParticipant { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? DueDate { get; set; }

    public Guid CreatedByParticipantId { get; set; }

    public BoardParticipant CreatedByParticipant { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = [];
}
