using System.Collections.Concurrent;
using System.Net;
using Contracts.Grpc.Flight;
using Contracts.Grpc.Passenger;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Booking.Host.Test;

// A real Kestrel h2c gRPC server on a loopback port, standing in for a separately deployed service, so
// Booking.Host's generated gRPC clients cross an actual HTTP/2 network hop.
public sealed class FakeGrpcServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private FakeGrpcServer(WebApplication app)
    {
        _app = app;
        Address = app
            .Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.Single();
    }

    public string Address { get; }

    public static async Task<FakeGrpcServer> StartAsync<TService>(TService service, string grpcServiceName)
        where TService : class
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel =>
            kestrel.Listen(IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http2)
        );

        builder.Services.AddSingleton(service);
        builder.Services.AddGrpc();
        builder.Services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy());
        builder.Services.AddGrpcHealthChecks(options => options.Services.Map(grpcServiceName, _ => true));

        var app = builder.Build();
        app.MapGrpcService<TService>();
        app.MapGrpcHealthChecksService();
        await app.StartAsync();

        return new FakeGrpcServer(app);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

public sealed class FakeFlightGrpcService : FlightGrpcService.FlightGrpcServiceBase
{
    public const string AvailableSeatNumber = "12C";

    public ConcurrentQueue<ReserveSeatRequest> ReservedSeats { get; } = new();

    public ConcurrentQueue<string> RequestedFlightIds { get; } = new();

    public override Task<GetFlightByIdResult> GetById(
        Contracts.Grpc.Flight.GetByIdRequest request,
        ServerCallContext context
    )
    {
        RequestedFlightIds.Enqueue(request.Id);

        return Task.FromResult(
            new GetFlightByIdResult
            {
                FlightDto = new FlightResponse
                {
                    Id = request.Id,
                    FlightNumber = "BK-496",
                    AircraftId = Guid.NewGuid().ToString(),
                    DepartureAirportId = Guid.NewGuid().ToString(),
                    ArriveAirportId = Guid.NewGuid().ToString(),
                    FlightDate = Timestamp.FromDateTime(DateTime.UtcNow.Date.AddDays(7)),
                    DepartureDate = Timestamp.FromDateTime(DateTime.UtcNow.Date.AddDays(7)),
                    ArriveDate = Timestamp.FromDateTime(DateTime.UtcNow.Date.AddDays(7).AddHours(2)),
                    DurationMinutes = 120,
                    Price = 250,
                },
            }
        );
    }

    public override Task<GetAvailableSeatsResult> GetAvailableSeats(
        GetAvailableSeatsRequest request,
        ServerCallContext context
    )
    {
        var result = new GetAvailableSeatsResult();
        result.SeatDtos.Add(
            new SeatDtoResponse
            {
                Id = Guid.NewGuid().ToString(),
                SeatNumber = AvailableSeatNumber,
                FlightId = request.FlightId,
            }
        );

        return Task.FromResult(result);
    }

    public override Task<ReserveSeatResult> ReserveSeat(ReserveSeatRequest request, ServerCallContext context)
    {
        ReservedSeats.Enqueue(request);

        return Task.FromResult(new ReserveSeatResult { Id = Guid.NewGuid().ToString() });
    }
}

public sealed class FakePassengerGrpcService : PassengerGrpcService.PassengerGrpcServiceBase
{
    public const string PassengerName = "Ada Lovelace";

    public ConcurrentQueue<string> RequestedPassengerIds { get; } = new();

    public override Task<GetPassengerByIdResult> GetById(
        Contracts.Grpc.Passenger.GetByIdRequest request,
        ServerCallContext context
    )
    {
        RequestedPassengerIds.Enqueue(request.Id);

        return Task.FromResult(
            new GetPassengerByIdResult
            {
                PassengerDto = new PassengerResponse
                {
                    Id = request.Id,
                    Name = PassengerName,
                    PassportNumber = "P-496",
                    Age = 36,
                    Email = "ada@example.com",
                },
            }
        );
    }
}
