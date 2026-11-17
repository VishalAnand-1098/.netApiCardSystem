using InviteCraft.Application.Features.Payments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InviteCraft.Infrastructure.Payments;

/// <summary>Periodically expires unpaid orders past their deadline and gives their coupon reservations back.</summary>
internal sealed class OrderExpiryWorker(
    IServiceScopeFactory scopes,
    IOptions<PaymentSettings> options,
    IOptions<Rendering.RenderSettings> hosting,
    ILogger<OrderExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!hosting.Value.Enabled)
            return; // background work is switched off together (tests)

        var interval = TimeSpan.FromSeconds(options.Value.ExpirySweepSeconds);
        using var timer = new PeriodicTimer(interval);
        try
        {
            do
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<IOrderExpiryService>().ExpireDueAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Order expiry sweep failed; will try again");
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
