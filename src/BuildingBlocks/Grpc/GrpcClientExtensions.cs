using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace BuildingBlocks.Grpc;

public static class GrpcClientExtensions
{
    public static IServiceCollection AddGrpcServiceClient<TClient>(
        this IServiceCollection services,
        IConfiguration configuration,
        string name
    )
        where TClient : ClientBase<TClient>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var key = $"Grpc:Clients:{name}";
        services
            .AddOptions<GrpcClientOptions>(name)
            .Configure(options =>
            {
                configuration.GetSection("Grpc:Resilience").Bind(options.Resilience);
                configuration.GetSection(key).Bind(options);
            })
            .Validate(
                options =>
                    Uri.TryCreate(options.Address, UriKind.Absolute, out var address)
                    && (address.Scheme == Uri.UriSchemeHttp || address.Scheme == Uri.UriSchemeHttps),
                $"{key}:Address is required and must be an absolute HTTP(S) URI."
            )
            .Validate(
                options => options.Resilience.MaxRetryAttempts > 0,
                $"{key}:Resilience:MaxRetryAttempts must be positive."
            )
            .Validate(
                options =>
                    options.Resilience.AttemptTimeout >= TimeSpan.FromMilliseconds(10)
                    && options.Resilience.TotalTimeout >= TimeSpan.FromMilliseconds(10),
                $"{key}:Resilience:AttemptTimeout and TotalTimeout must be at least 10ms."
            )
            .Validate(
                options =>
                    options.Resilience.CircuitBreaker.SamplingDuration >= TimeSpan.FromMilliseconds(500)
                    && options.Resilience.CircuitBreaker.BreakDuration >= TimeSpan.FromMilliseconds(500)
                    && options.Resilience.CircuitBreaker.FailureRatio > 0
                    && options.Resilience.CircuitBreaker.FailureRatio <= 1,
                $"{key}:Resilience:CircuitBreaker requires durations of at least 500ms and FailureRatio in (0, 1]."
            )
            .ValidateOnStart();

        services
            .AddGrpcClient<TClient>(
                (provider, options) =>
                    options.Address = new Uri(
                        provider.GetRequiredService<IOptionsMonitor<GrpcClientOptions>>().Get(name).Address
                    )
            )
            .ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan)
            .AddResilienceHandler(
                $"grpc-{name}",
                (pipeline, context) =>
                {
                    var options = context
                        .ServiceProvider.GetRequiredService<IOptionsMonitor<GrpcClientOptions>>()
                        .Get(name)
                        .Resilience;
                    pipeline.AddTimeout(new HttpTimeoutStrategyOptions { Timeout = options.TotalTimeout });
                    pipeline.AddRetry(new HttpRetryStrategyOptions { MaxRetryAttempts = options.MaxRetryAttempts });
                    pipeline.AddCircuitBreaker(
                        new HttpCircuitBreakerStrategyOptions
                        {
                            SamplingDuration = options.CircuitBreaker.SamplingDuration,
                            FailureRatio = options.CircuitBreaker.FailureRatio,
                            BreakDuration = options.CircuitBreaker.BreakDuration,
                        }
                    );
                    pipeline.AddTimeout(new HttpTimeoutStrategyOptions { Timeout = options.AttemptTimeout });
                }
            );
        return services;
    }
}
