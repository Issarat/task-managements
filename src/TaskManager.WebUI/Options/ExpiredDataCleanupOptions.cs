namespace TaskManager.WebUI.Options;

public sealed class ExpiredDataCleanupOptions
{
    public const string SectionName = "ExpiredDataCleanup";
    public bool Enabled { get; set; } = true;
    public int IntervalMinutes { get; set; } = 5;
    public int BatchSize { get; set; } = 100;
}
