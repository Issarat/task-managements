namespace TaskManager.Domain.Entities;

public sealed class Board
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string PublicId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string OwnerTokenHash { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAtUtc { get; set; }

    public DateTime LastActivityAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public ICollection<BoardParticipant> Participants { get; set; } = [];

    public ICollection<BoardColumn> Columns { get; set; } = [];

    public ICollection<TaskItem> Tasks { get; set; } = [];

    public bool IsExpired(DateTime utcNow)
    {
        return ExpiresAtUtc <= utcNow;
    }

    public void MarkActivity(DateTime utcNow)
    {
        LastActivityAtUtc = utcNow;
    }
}
