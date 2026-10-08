namespace TaskManager.Domain.Entities;

public sealed class AnonymousUser
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string SessionTokenHash { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string AvatarColor { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime LastSeenAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public ICollection<BoardParticipant> BoardParticipants { get; set; } = [];

    public bool IsExpired(DateTime utcNow)
    {
        return ExpiresAtUtc <= utcNow;
    }
}
