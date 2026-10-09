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
    IReadOnlyList<BoardColumnViewModel> Columns);

public sealed record BoardNavigationViewModel(
    string PublicId,
    string Name,
    bool IsActive);

public sealed record BoardMemberViewModel(
    string ShortName,
    string DisplayName,
    string Color);

public sealed record BoardColumnViewModel(
    string Name,
    string Key,
    string Tone,
    IReadOnlyList<BoardTaskCardViewModel> Tasks);

public sealed record BoardTaskCardViewModel(
    string Title,
    string Description,
    string Priority,
    string PriorityTone,
    string AssigneeShortName,
    string AssigneeDisplayName,
    string AssigneeColor,
    DateOnly? StartDate,
    DateOnly? DueDate);
