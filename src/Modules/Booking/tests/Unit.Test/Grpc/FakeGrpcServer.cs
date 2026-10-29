using Flight;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Passenger;

namespace Unit.Test.Grpc;

/// <summary>Real Kestrel h2c server (separate socket, real HTTP/2) hosting fake Flight/Passenger services and grpc.health.v1.</summary>
public sealed class FakeGrpcServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private FakeGrpcServer(WebApplication app, FakeFlightService flight, FakePassengerService passenger)
    {
        _app = app;
        Flight = flight;
        Passenger = passenger;
    }

    public FakeFlightService Flight { get; }
    public FakePassengerService Passenger { get; }

    public string Address => _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

    public static async Task<FakeGrpcServer> StartAsync()
    {
        var flight = new FakeFlightService();
        var passenger = new FakePassengerService();

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k => k.Listen(System.Net.IPAddress.Loopback, 0, o => o.Protocols = HttpProtocols.Http2));
        builder.Services.AddGrpc();
        builder.Services.AddGrpcHealthChecks().AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy());
        builder.Services.AddSingleton(flight);
        builder.Services.AddSingleton(passenger);

        var app = builder.Build();
        app.MapGrpcService<FakeFlightService>();
        app.MapGrpcService<FakePassengerService>();
        app.MapGrpcHealthChecksService();
        await app.StartAsync();

        return new FakeGrpcServer(app, flight, passenger);
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
    private int _reserveSeatCalls;
    private int _getAvailableSeatsCalls;

    /// <summary>Number of leading GetById calls answered with Unavailable.</summary>
    public int GetByIdFailures { get; set; }
    public bool ReserveSeatUnavailable { get; set; }
    public bool HangGetAvailableSeats { get; set; }

    public int GetByIdCalls => _getByIdCalls;
    public int ReserveSeatCalls => _reserveSeatCalls;
    public int GetAvailableSeatsCalls => _getAvailableSeatsCalls;
    public DateTime? LastDeadline { get; private set; }

    public override Task<GetFlightByIdResult> GetById(Flight.GetByIdRequest request, ServerCallContext context)
    {
        LastDeadline = context.Deadline;
        if (Interlocked.Increment(ref _getByIdCalls) <= GetByIdFailures)
            throw new RpcException(new Status(StatusCode.Unavailable, "flight warming up"));

        return Task.FromResult(new GetFlightByIdResult { FlightDto = new FlightResponse { Id = request.Id, FlightNumber = "FL-1" } });
    }

    public override async Task<GetAvailableSeatsResult> GetAvailableSeats(GetAvailableSeatsRequest request, ServerCallContext context)
    {
        Interlocked.Increment(ref _getAvailableSeatsCalls);
        if (HangGetAvailableSeats)
            await Task.Delay(Timeout.Infinite, context.CancellationToken);

        return new GetAvailableSeatsResult();
    }

    public override Task<ReserveSeatResult> ReserveSeat(ReserveSeatRequest request, ServerCallContext context)
    {
        Interlocked.Increment(ref _reserveSeatCalls);
        if (ReserveSeatUnavailable)
            throw new RpcException(new Status(StatusCode.Unavailable, "flight unavailable"));

        return Task.FromResult(new ReserveSeatResult { Id = request.FlightId });
    }
}

public sealed class FakePassengerService : PassengerGrpcService.PassengerGrpcServiceBase
{
    private int _getByIdCalls;

    public int GetByIdFailures { get; set; }
    public int GetByIdCalls => _getByIdCalls;

    public override Task<GetPassengerByIdResult> GetById(Passenger.GetByIdRequest request, ServerCallContext context)
    {
        if (Interlocked.Increment(ref _getByIdCalls) <= GetByIdFailures)
            throw new RpcException(new Status(StatusCode.Unavailable, "passenger warming up"));

        return Task.FromResult(new GetPassengerByIdResult { PassengerDto = new PassengerResponse { Id = request.Id } });
    }
}
