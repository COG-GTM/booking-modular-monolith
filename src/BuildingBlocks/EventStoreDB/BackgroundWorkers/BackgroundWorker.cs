using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.EventStoreDB.BackgroundWorkers;

public class BackgroundWorker : BackgroundService
{
    private readonly ILogger<BackgroundWorker> logger;
    private readonly Func<CancellationToken, Task> perform;

    public BackgroundWorker(ILogger<BackgroundWorker> logger, Func<CancellationToken, Task> perform)
    {
        this.logger = logger;
        this.perform = perform;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Background worker started");

        try
        {
            await perform(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }

        logger.LogInformation("Background worker stopped");
    }
}
