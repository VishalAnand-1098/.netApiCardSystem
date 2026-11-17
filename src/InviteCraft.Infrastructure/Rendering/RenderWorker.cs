using System.Threading.Channels;
using InviteCraft.Application.Features.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InviteCraft.Infrastructure.Rendering;

/// <summary>In-process job queue. The durable state is the <c>downloads</c> row, so a restart loses nothing (see <see cref="RenderWorker"/>).</summary>
internal sealed class RenderQueue : IRenderQueue
{
    private readonly Channel<Guid> channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = false });

    public ValueTask EnqueueAsync(Guid downloadId, CancellationToken cancellationToken = default) =>
        channel.Writer.WriteAsync(downloadId, cancellationToken);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) => channel.Reader.ReadAllAsync(cancellationToken);
}

/// <summary>
/// Background workers that turn queued downloads into files. On start-up it re-queues anything left Queued or
/// Rendering by a previous run, so no customer request is dropped by a deployment or crash.
/// </summary>
internal sealed class RenderWorker(
    RenderQueue queue,
    IServiceScopeFactory scopes,
    IOptions<RenderSettings> options,
    TimeProvider timeProvider,
    ILogger<RenderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Render workers are disabled (Rendering:Enabled = false)");
            return;
        }

        try
        {
            await RequeueUnfinishedAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The database may not be reachable yet (or not migrated in a test host); workers still start.
            logger.LogWarning(ex, "Could not re-queue unfinished downloads at start-up");
        }

        var workers = Enumerable.Range(0, Math.Max(1, options.Value.MaxConcurrency)).Select(_ => RunAsync(stoppingToken));
        await Task.WhenAll(workers);
    }

    private async Task RequeueUnfinishedAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var downloads = scope.ServiceProvider.GetRequiredService<IDownloadRepository>();
        var ids = await downloads.GetUnfinishedIdsAsync(timeProvider.GetUtcNow(), cancellationToken);
        foreach (var id in ids)
            await queue.EnqueueAsync(id, cancellationToken);
        if (ids.Count > 0)
            logger.LogInformation("Re-queued {Count} unfinished download(s)", ids.Count);
    }

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var id in queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<IRenderJobProcessor>().ProcessAsync(id, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Unhandled error while processing download {DownloadId}", id);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
