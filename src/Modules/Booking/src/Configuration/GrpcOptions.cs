namespace Booking.Configuration;

/// <summary>
/// Booking's downstream gRPC endpoints (section <c>Grpc</c>). Addresses are logical: <c>http://_grpc.flight</c> is
/// resolved by service discovery from <c>Services:flight:grpc:0</c> (Aspire injects <c>services__flight__grpc__0</c>),
/// while a concrete URL such as <c>http://flight-service:81</c> (K8s/compose DNS name) is used as-is.
/// </summary>
public class GrpcOptions
{
    public const string SectionName = "Grpc";

    public GrpcEndpointOptions Flight { get; set; } = new();
    public GrpcEndpointOptions Passenger { get; set; } = new();

    /// <summary>Reject plain-text (<c>http://</c>) addresses at startup; set in environments that terminate TLS at the service.</summary>
    public bool RequireTls { get; set; }
}

public class GrpcEndpointOptions
{
    public string? Address { get; set; }

    /// <summary>Default deadline for a call that does not set one; covers every retry attempt.</summary>
    public TimeSpan Deadline { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Total attempts (first call included) for idempotent reads on <c>Unavailable</c>; 1 disables retries.</summary>
    public int MaxAttempts { get; set; } = 4;

    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromMilliseconds(200);

    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Deadline of the readiness probe (<c>grpc.health.v1.Health/Check</c>) against this dependency.</summary>
    public TimeSpan HealthCheckTimeout { get; set; } = TimeSpan.FromSeconds(2);
}
