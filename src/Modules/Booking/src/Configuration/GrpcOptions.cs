namespace Booking.Configuration;

/// <summary>
/// gRPC service endpoints for cross-service communication.
/// In microservices mode, use Aspire service discovery URIs (e.g., "https+http://flight-api").
/// In monolith mode, these resolve to localhost since all services share the same process.
/// </summary>
public class GrpcOptions
{
    public string FlightAddress { get; set; }
    public string PassengerAddress { get; set; }
}