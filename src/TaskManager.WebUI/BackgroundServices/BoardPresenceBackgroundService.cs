using Microsoft.AspNetCore.SignalR;
using TaskManager.WebUI.Hubs;
using TaskManager.WebUI.Services;

namespace TaskManager.WebUI.BackgroundServices;

public sealed class BoardPresenceBackgroundService(
    BoardPresenceService presence,
    IHubContext<BoardHub> hubContext,
    TimeProvider timeProvider,
    ILogger<BoardPresenceBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var expired = presence.RemoveExpired();
                foreach (var connection in expired.Connections)
                {
                    await hubContext.Groups.RemoveFromGroupAsync(connection.ConnectionId,
                        $"board:{connection.PublicId}", stoppingToken);
                    await hubContext.Clients.Client(connection.ConnectionId)
                        .SendAsync("BoardSessionExpired", stoppingToken);
                }
                foreach (var snapshot in expired.Snapshots)
                    await hubContext.Clients.Group($"board:{snapshot.PublicId}")
                        .SendAsync("BoardPresenceChanged", snapshot, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Could not publish expired board presence.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
