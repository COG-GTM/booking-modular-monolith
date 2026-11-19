namespace BuildingBlocks.Grpc;

public sealed class GrpcClientOptions
{
    public string Address { get; set; } = string.Empty;
    public GrpcResilienceOptions Resilience { get; set; } = new();
}

public sealed class GrpcResilienceOptions
{
    public int MaxRetryAttempts { get; set; } = 3;
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromMinutes(3);
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromMinutes(3);
    public GrpcCircuitBreakerOptions CircuitBreaker { get; set; } = new();
}

public sealed class GrpcCircuitBreakerOptions
{
    public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromMinutes(2);
    public double FailureRatio { get; set; } = 0.1;
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(5);
}
