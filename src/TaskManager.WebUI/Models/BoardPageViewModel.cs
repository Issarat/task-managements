using TaskManager.Application.Models;

namespace TaskManager.WebUI.Models;

public sealed record BoardPageViewModel(
    string PublicId,
    string Name,
    string Description,
    bool CanManage,
    GuestResponseModel CurrentGuest,
    IReadOnlyList<BoardNavigationViewModel> NavigationBoards,
    IReadOnlyList<BoardMemberViewModel> Members,
    IReadOnlyList<BoardColumnViewModel> Columns)
{
    public CreateTaskFormModel CreateTask { get; init; } = new();
    public bool ShowCreateTaskDialog { get; init; }
    public EditTaskFormModel EditTask { get; init; } = new();
    public bool ShowEditTaskDialog { get; init; }
    public string BoardRowVersion { get; init; } = string.Empty;
    public EditBoardFormModel EditBoard { get; init; } = new();
    public bool ShowEditBoardDialog { get; init; }
    public bool BoardConflict { get; init; }
    public CreateBoardFormModel CreateBoard { get; init; } = new();
    public bool ShowCreateBoardDialog { get; init; }
}

public sealed record BoardNavigationViewModel(
    string PublicId,
    string Name,
    bool IsActive);

public sealed record BoardMemberViewModel(
    Guid ParticipantId,
    string ShortName,
    string DisplayName,
    string Color);

public sealed record BoardColumnViewModel(
    Guid ColumnId,
    string Name,
    string Key,
    string Tone,
    IReadOnlyList<BoardTaskCardViewModel> Tasks);

public sealed record BoardTaskCardViewModel(
    Guid TaskItemId,
    string Title,
    string Description,
    string Priority,
    string PriorityTone,
    string AssigneeShortName,
    string AssigneeDisplayName,
    string AssigneeColor,
    DateOnly? StartDate,
    DateOnly? DueDate,
    string RowVersion);
