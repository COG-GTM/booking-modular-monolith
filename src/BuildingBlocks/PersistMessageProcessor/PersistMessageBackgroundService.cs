using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.PersistMessageProcessor;

public abstract class PersistMessageBackgroundService(
    ILogger logger,
    IServiceProvider serviceProvider,
    IOptions<PersistMessageOptions> options
) : BackgroundService
{
    private PersistMessageOptions _options = options.Value;

    protected abstract string ModuleName { get; }

    protected abstract IPersistMessageProcessor GetProcessor(IServiceProvider scopedServiceProvider);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("PersistMessage Background Service for {Module} Start", ModuleName);

        await ProcessAsync(stoppingToken);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("PersistMessage Background Service for {Module} Stop", ModuleName);

        return base.StopAsync(cancellationToken);
    }

    private async Task ProcessAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await using (var scope = serviceProvider.CreateAsyncScope())
            {
                var service = GetProcessor(scope.ServiceProvider);
                await service.ProcessAllAsync(stoppingToken);
            }

            var delay = _options.Interval is { }
                ? TimeSpan.FromSeconds((int)_options.Interval)
                : TimeSpan.FromSeconds(30);

            await Task.Delay(delay, stoppingToken);
        }
    }
}

// Polls only the outbox/inbox store owned by TModule.
public class PersistMessageBackgroundService<TModule>(
    ILogger<PersistMessageBackgroundService<TModule>> logger,
    IServiceProvider serviceProvider,
    IOptions<PersistMessageOptions> options
) : PersistMessageBackgroundService(logger, serviceProvider, options)
    where TModule : class
{
    protected override string ModuleName => typeof(TModule).Name;

    protected override IPersistMessageProcessor GetProcessor(IServiceProvider scopedServiceProvider) =>
        scopedServiceProvider.GetRequiredService<IPersistMessageProcessor<TModule>>();
}
