using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.PersistMessageProcessor;

public class PersistMessageBackgroundService(
    ILogger<PersistMessageBackgroundService> logger,
    IServiceProvider serviceProvider,
    IOptions<PersistMessageOptions> options
)
    : BackgroundService
{
    private PersistMessageOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("PersistMessage Background Service Start");

        await ProcessAsync(stoppingToken);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("PersistMessage Background Service Stop");

        return base.StopAsync(cancellationToken);
    }

    private async Task ProcessAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = serviceProvider.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<IPersistMessageProcessor>();
                await service.ProcessAllAsync(stoppingToken);
            }
            catch (System.Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "PersistMessage Background Service cycle failed; retrying on next interval");
            }

            var delay = _options.Interval is { }
                            ? TimeSpan.FromSeconds((int)_options.Interval)
                            : TimeSpan.FromSeconds(30);

            await Task.Delay(delay, stoppingToken);
        }
    }
}