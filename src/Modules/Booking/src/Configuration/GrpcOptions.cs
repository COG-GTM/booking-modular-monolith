using BuildingBlocks.Grpc;

namespace Booking.Configuration;

public class GrpcOptions
{
    public GrpcClientOptions Flight { get; set; } = new() { Address = "https://flight" };
    public GrpcClientOptions Passenger { get; set; } = new() { Address = "https://passenger" };
}
