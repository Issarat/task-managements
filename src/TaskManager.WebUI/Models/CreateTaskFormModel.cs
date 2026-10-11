using TaskManager.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace TaskManager.WebUI.Models;

public sealed class CreateTaskFormModel
{
    public string Title { get; set; } = string.Empty;
    public string? DescriptionMarkdown { get; set; }
    public TaskPriority Priority { get; set; } = TaskPriority.Low;
    public Guid? AssignedToParticipantId { get; set; }
    [ModelBinder(BinderType = typeof(IsoDateOnlyModelBinder))]
    public DateOnly? StartDate { get; set; }

    [ModelBinder(BinderType = typeof(IsoDateOnlyModelBinder))]
    public DateOnly? DueDate { get; set; }
}
