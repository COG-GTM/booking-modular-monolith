namespace Booking.Configuration;

public class GrpcOptions
{
    public string FlightAddress { get; set; } = default!;
    public string PassengerAddress { get; set; } = default!;
    public TimeSpan Deadline { get; set; } = TimeSpan.FromSeconds(10);
    // Total attempts including the initial call; 1 disables retries.
    public int MaxRetryAttempts { get; set; } = 3;
    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromMilliseconds(200);
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(2);
    public bool HealthChecksEnabled { get; set; } = true;
}
