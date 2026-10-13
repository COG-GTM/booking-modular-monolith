namespace Booking.Configuration;

public class GrpcOptions
{
    public const string SectionName = "Grpc";

    // Plain http(s) URLs: a real endpoint (https://localhost:5201, http://flight-service:81) or an
    // Aspire/service-discovery logical name (https://flight resolved from services:flight:https:0).
    public string? FlightAddress { get; set; }

    public string? PassengerAddress { get; set; }

    // Applied to every call that doesn't set its own deadline.
    public TimeSpan Deadline { get; set; } = TimeSpan.FromSeconds(10);

    // gRPC retry policy (status Unavailable); includes the original attempt, 1 disables retries.
    public int MaxAttempts { get; set; } = 3;

    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromMilliseconds(200);

    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(2);

    // Development only: accept self-signed server certificates on https addresses.
    public bool AllowUntrustedServerCertificate { get; set; }

    // Registers grpc.health.v1 readiness checks (tags "ready", "grpc") for Flight and Passenger.
    public bool HealthChecksEnabled { get; set; } = true;
}
