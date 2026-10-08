using TaskManager.Application.Models;

namespace TaskManager.Application.Interfaces;

public interface IGuestService
{
    Task<CreatedGuestModel> GenerateGuestAsync(
        CancellationToken cancellationToken);

    Task<GuestResponseModel?> GetGuestByTokenAsync(
        string guestToken,
        CancellationToken cancellationToken);
}
