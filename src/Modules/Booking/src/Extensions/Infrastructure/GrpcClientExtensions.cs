using Booking.Configuration;
using BuildingBlocks.Grpc;
using BuildingBlocks.Web;
using Contracts.Grpc.Flight.V1;
using Contracts.Grpc.Passenger.V1;
using Microsoft.Extensions.DependencyInjection;

namespace Booking.Extensions.Infrastructure;

public static class GrpcClientExtensions
{
    private const string FlightClientName = "flight";
    private const string PassengerClientName = "passenger";

    public static IServiceCollection AddGrpcClients(this IServiceCollection services)
    {
        var grpcOptions = services.GetOptions<GrpcOptions>("Grpc");

        services.AddResilientGrpcClient<FlightGrpcService.FlightGrpcServiceClient>(
            FlightClientName,
            grpcOptions.Flight
        );
        services.AddResilientGrpcClient<PassengerGrpcService.PassengerGrpcServiceClient>(
            PassengerClientName,
            grpcOptions.Passenger
        );

        services
            .AddHealthChecks()
            .AddGrpcServiceHealthCheck(FlightClientName, grpcOptions.Flight)
            .AddGrpcServiceHealthCheck(PassengerClientName, grpcOptions.Passenger);

        return services;
    }
}
