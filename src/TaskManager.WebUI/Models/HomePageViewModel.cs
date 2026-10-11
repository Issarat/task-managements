using TaskManager.Application.Models;

namespace TaskManager.WebUI.Models;

public sealed record HomePageViewModel(
    GuestResponseModel? Guest,
    IReadOnlyList<BoardNavigationModel> Boards)
{
    public BoardModel? SharedBoard { get; init; }
    public bool SharedBoardUnavailable { get; init; }
}
