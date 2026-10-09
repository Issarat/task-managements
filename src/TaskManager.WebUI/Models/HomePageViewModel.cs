using TaskManager.Application.Models;

namespace TaskManager.WebUI.Models;

public sealed record HomePageViewModel(
    GuestResponseModel? Guest,
    IReadOnlyList<BoardNavigationModel> Boards);
