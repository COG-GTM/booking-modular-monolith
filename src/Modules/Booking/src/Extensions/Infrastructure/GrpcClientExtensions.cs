using BuildingBlocks.Grpc;
using Contracts.Grpc.Flight;
using Contracts.Grpc.Passenger;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Booking.Extensions.Infrastructure;

public static class GrpcClientExtensions
{
    public static IServiceCollection AddGrpcClients(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddGrpcServiceClient<FlightGrpcService.FlightGrpcServiceClient>(configuration, "Flight");
        services.AddGrpcServiceClient<PassengerGrpcService.PassengerGrpcServiceClient>(configuration, "Passenger");
        return services;
    }
}
