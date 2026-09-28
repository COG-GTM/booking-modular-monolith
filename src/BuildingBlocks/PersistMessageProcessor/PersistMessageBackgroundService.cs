using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.PersistMessageProcessor;

public class PersistMessageBackgroundService<TModule>(
    ILogger<PersistMessageBackgroundService<TModule>> logger,
    IServiceProvider serviceProvider,
    IOptions<PersistMessageOptions> options
)
    : BackgroundService
    where TModule : class
{
    private PersistMessageOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("PersistMessage Background Service for {Module} Start", typeof(TModule).Name);

        await ProcessAsync(stoppingToken);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("PersistMessage Background Service for {Module} Stop", typeof(TModule).Name);

        return base.StopAsync(cancellationToken);
    }

    private async Task ProcessAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await using (var scope = serviceProvider.CreateAsyncScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<IPersistMessageProcessor<TModule>>();
                await service.ProcessAllAsync(stoppingToken);
            }

            var delay = _options.Interval is { }
                            ? TimeSpan.FromSeconds((int)_options.Interval)
                            : TimeSpan.FromSeconds(30);

            await Task.Delay(delay, stoppingToken);
        }
    }
}
