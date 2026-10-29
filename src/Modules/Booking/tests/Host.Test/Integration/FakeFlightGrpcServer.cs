using System.Net;
using Flight;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Host.Test.Integration;

public sealed class FakeFlightGrpcServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private FakeFlightGrpcServer(WebApplication app, FakeFlightService service, string address)
    {
        _app = app;
        Service = service;
        Address = address;
    }

    public string Address { get; }
    public FakeFlightService Service { get; }

    public static async Task<FakeFlightGrpcServer> StartAsync()
    {
        var service = new FakeFlightService();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(options =>
            options.Listen(IPAddress.Loopback, 0, listenOptions => listenOptions.Protocols = HttpProtocols.Http2)
        );
        builder.Services.AddGrpc();
        builder.Services.AddSingleton(service);

        var app = builder.Build();
        app.MapGrpcService<FakeFlightService>();
        await app.StartAsync();

        var address = app
            .Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();
        return new FakeFlightGrpcServer(app, service, address);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

public sealed class FakeFlightService : FlightGrpcService.FlightGrpcServiceBase
{
    private int _getByIdCalls;
    private int _getAvailableSeatsCalls;
    private int _reserveSeatCalls;

    public int GetByIdCalls => _getByIdCalls;
    public int GetAvailableSeatsCalls => _getAvailableSeatsCalls;
    public int ReserveSeatCalls => _reserveSeatCalls;

    public override Task<GetFlightByIdResult> GetById(Flight.GetByIdRequest request, ServerCallContext context)
    {
        Interlocked.Increment(ref _getByIdCalls);
        return Task.FromResult(
            new GetFlightByIdResult
            {
                FlightDto = new FlightResponse
                {
                    Id = request.Id,
                    FlightNumber = "HOST-TEST-1",
                    AircraftId = Guid.NewGuid().ToString(),
                    DepartureAirportId = Guid.NewGuid().ToString(),
                    ArriveAirportId = Guid.NewGuid().ToString(),
                    FlightDate = Timestamp.FromDateTime(DateTime.UtcNow.AddDays(2)),
                    Price = 100,
                },
            }
        );
    }

    public override Task<GetAvailableSeatsResult> GetAvailableSeats(
        GetAvailableSeatsRequest request,
        ServerCallContext context
    )
    {
        Interlocked.Increment(ref _getAvailableSeatsCalls);
        return Task.FromResult(
            new GetAvailableSeatsResult
            {
                SeatDtos =
                {
                    new SeatDtoResponse
                    {
                        Id = Guid.NewGuid().ToString(),
                        SeatNumber = "1A",
                        FlightId = request.FlightId,
                    },
                },
            }
        );
    }

    public override Task<ReserveSeatResult> ReserveSeat(ReserveSeatRequest request, ServerCallContext context)
    {
        Interlocked.Increment(ref _reserveSeatCalls);
        return Task.FromResult(new ReserveSeatResult { Id = request.FlightId });
    }
}
