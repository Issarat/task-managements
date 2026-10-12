namespace TaskManager.WebUI.Models;

public sealed class EditBoardFormModel
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
