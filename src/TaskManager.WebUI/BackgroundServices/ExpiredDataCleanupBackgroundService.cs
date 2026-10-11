using Microsoft.Extensions.Options;
using TaskManager.Application.Interfaces;
using TaskManager.WebUI.Options;

namespace TaskManager.WebUI.BackgroundServices;

public sealed class ExpiredDataCleanupBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<ExpiredDataCleanupOptions> options,
    TimeProvider timeProvider,
    ILogger<ExpiredDataCleanupBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("Expired-data cleanup is disabled.");
            return;
        }

        // Run once on startup, then periodically while the application is running.
        // Expiration is enforced by the application even between cleanup runs.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var cleanup = scope.ServiceProvider.GetRequiredService<IExpiredDataCleanupService>();
                var result = await cleanup.CleanupAsync(timeProvider.GetUtcNow().UtcDateTime,
                    settings.BatchSize, stoppingToken);
                if (result.TotalDeleted > 0)
                {
                    logger.LogInformation(
                        "Expired-data cleanup deleted {Boards} boards, {Tasks} tasks, {Participants} participants, {Columns} columns and {Guests} guests.",
                        result.DeletedBoards, result.DeletedTasks, result.DeletedParticipants,
                        result.DeletedColumns, result.DeletedGuests);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // A failed transaction is rolled back; retry on the next interval.
                logger.LogError(exception, "Expired-data cleanup failed. It will retry on the next interval.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(settings.IntervalMinutes), timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
