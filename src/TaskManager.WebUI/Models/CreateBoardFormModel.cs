namespace TaskManager.WebUI.Models;

public sealed class CreateBoardFormModel
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? ReturnBoardPublicId { get; set; }
}
