using TaskManager.Application.Models;

namespace TaskManager.Application.Interfaces;

public interface IBoardService
{
    Task<CreatedBoardModel> CreateAsync(
        CreateBoardRequestModel request,
        CancellationToken cancellationToken = default);

    Task<BoardResponseModel?> GetAsync(
        string publicId,
        Guid anonymousUserId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BoardNavigationModel>> GetListBySessionIdAsync(
        Guid anonymousUserId,
        CancellationToken cancellationToken = default);

    Task<BoardModel?> UpdateAsync(
        string publicId,
        string ownerToken,
        UpdateBoardRequestModel request,
        CancellationToken cancellationToken = default);
}
