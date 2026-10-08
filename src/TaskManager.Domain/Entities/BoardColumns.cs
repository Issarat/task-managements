namespace TaskManager.Domain.Entities;

public sealed class BoardColumn
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BoardId { get; set; }

    public Board Board { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    public string ColumnKey { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public ICollection<TaskItem> Tasks { get; set; } = [];
}
