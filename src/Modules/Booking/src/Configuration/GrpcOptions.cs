namespace Booking.Configuration;

/// <summary>
/// gRPC service addresses. In microservices mode, use Aspire service discovery URIs
/// (e.g., "https+http://flight-api") instead of localhost addresses.
/// </summary>
public class GrpcOptions
{
    public string FlightAddress { get; set; } = "https+http://flight-api";
    public string PassengerAddress { get; set; } = "https+http://passenger-api";
}
