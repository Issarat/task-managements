using TaskManager.Domain.Enums;
namespace TaskManager.WebUI.Extensions;

public static class TaskPriorityPresentationExtensions
{
    /// <summary>
    /// แปลง Priority เป็นข้อความภาษาไทยและ CSS class สำหรับแสดงผล
    /// </summary>
    public static PriorityPresentation ToPresentation(
        this TaskPriority priority)
    {
        return priority switch
        {
            TaskPriority.Low =>
                new PriorityPresentation("ต่ำ", "priority-low"),

            TaskPriority.Medium =>
                new PriorityPresentation("ปานกลาง", "priority-medium"),

            TaskPriority.High =>
                new PriorityPresentation("สูง", "priority-high"),

            _ => throw new ArgumentOutOfRangeException(
                nameof(priority),
                priority,
                "ไม่รองรับค่า Priority นี้")
        };
    }
}

public sealed record PriorityPresentation(
    string ThaiText,
    string CssClass);