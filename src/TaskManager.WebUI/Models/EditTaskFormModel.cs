namespace TaskManager.WebUI.Models;

public sealed class EditTaskFormModel : CreateTaskFormModel
{
    public Guid TaskItemId { get; set; }
    public Guid BoardColumnId { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
