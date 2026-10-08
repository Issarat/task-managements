namespace TaskManager.Domain.Entities;

public sealed class BoardParticipant
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BoardId { get; set; }

    public Board Board { get; set; } = null!;

    public Guid AnonymousUserId { get; set; }

    public AnonymousUser AnonymousUser { get; set; } = null!;

    public DateTime JoinedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime LastSeenAtUtc { get; set; } = DateTime.UtcNow;

    public byte[] RowVersion { get; set; } = [];

    public ICollection<TaskItem> CreatedTasks { get; set; } = [];

    public ICollection<TaskItem> AssignedTasks { get; set; } = [];

    public void MarkSeen(DateTime utcNow)
    {
        LastSeenAtUtc = utcNow;
    }
}
