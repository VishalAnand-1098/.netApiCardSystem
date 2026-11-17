using InviteCraft.Application.Features.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InviteCraft.Infrastructure.Notifications;

/// <summary>Periodically sends pending email notifications (receipts, refund notices).</summary>
internal sealed class NotificationEmailWorker(
    IServiceScopeFactory scopes,
    IOptions<NotificationSettings> options,
    IOptions<Rendering.RenderSettings> hosting,
    ILogger<NotificationEmailWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!hosting.Value.Enabled)
            return; // background work is switched off together (tests)

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, options.Value.SweepSeconds)));
        try
        {
            do
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<INotificationDispatcher>().DispatchDueAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Notification email sweep failed; will try again");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }
}
