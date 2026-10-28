using System.Collections.Concurrent;
using System.Net;
using Booking.Data;
using BuildingBlocks.TestBase;
using Contracts.Grpc.Flight;
using Contracts.Grpc.Passenger;
using Google.Protobuf.WellKnownTypes;
using Grpc.AspNetCore.HealthChecks;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Host.Test;

public class BookingHostFixture : TestReadFixture<Booking.Host.Program, BookingReadDbContext>, IAsyncLifetime
{
    private readonly FakeGrpcServer grpcServer = new();
    private readonly Dictionary<string, string?> originalEnvironment = new();

    public FakeGrpcServer GrpcServer => grpcServer;

    async Task IAsyncLifetime.InitializeAsync()
    {
        await grpcServer.StartAsync();
        SetEnvironment("Grpc__FlightAddress", grpcServer.Address);
        SetEnvironment("Grpc__PassengerAddress", grpcServer.Address);
        await base.InitializeAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await grpcServer.DisposeAsync();
        foreach (var (key, value) in originalEnvironment)
            Environment.SetEnvironmentVariable(key, value);
    }

    private void SetEnvironment(string key, string value)
    {
        originalEnvironment[key] = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, value);
    }
}

public sealed class FakeGrpcServer : IAsyncDisposable
{
    private readonly ConcurrentQueue<string> calls = new();
    private WebApplication? app;

    public string Address { get; private set; } = "";
    public IReadOnlyCollection<string> Calls => calls.ToArray();
    public string FlightNumber => FakeFlightService.FlightNumber;
    public Guid AircraftId => FakeFlightService.AircraftId;
    public Guid DepartureAirportId => FakeFlightService.DepartureAirportId;
    public Guid ArriveAirportId => FakeFlightService.ArriveAirportId;
    public DateTime FlightDate => FakeFlightService.FlightDate;

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(options =>
            options.Listen(IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http2)
        );
        builder.Services.AddGrpc();
        builder
            .Services.AddHealthChecks()
            .AddCheck("flight", () => HealthCheckResult.Healthy(), tags: ["flight"])
            .AddCheck("passenger", () => HealthCheckResult.Healthy(), tags: ["passenger"]);
        builder.Services.AddGrpcHealthChecks(options =>
        {
            options.Services.Map("flight.FlightGrpcService", check => check.Tags.Contains("flight"));
            options.Services.Map("passenger.PassengerGrpcService", check => check.Tags.Contains("passenger"));
        });

        app = builder.Build();
        app.MapGrpcService<FakeFlightService>();
        app.MapGrpcService<FakePassengerService>();
        app.MapGrpcHealthChecksService();
        await app.StartAsync();
        Address = app
            .Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.Single();
        FakeFlightService.Calls = calls;
        FakePassengerService.Calls = calls;
    }

    public async ValueTask DisposeAsync()
    {
        if (app is not null)
            await app.DisposeAsync();
    }

    private sealed class FakeFlightService : FlightGrpcService.FlightGrpcServiceBase
    {
        public static ConcurrentQueue<string> Calls { get; set; } = new();
        public static string FlightNumber => "1500B";
        public static Guid AircraftId => Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        public static Guid DepartureAirportId => Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        public static Guid ArriveAirportId => Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        public static DateTime FlightDate => new(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        public override Task<GetFlightByIdResult> GetById(
            Contracts.Grpc.Flight.GetByIdRequest request,
            ServerCallContext context
        )
        {
            Calls.Enqueue("flight.GetById");
            return Task.FromResult(
                new GetFlightByIdResult
                {
                    FlightDto = new FlightResponse
                    {
                        Id = request.Id,
                        FlightNumber = FlightNumber,
                        AircraftId = AircraftId.ToString(),
                        DepartureAirportId = DepartureAirportId.ToString(),
                        ArriveAirportId = ArriveAirportId.ToString(),
                        DepartureDate = Timestamp.FromDateTime(DateTime.UtcNow),
                        ArriveDate = Timestamp.FromDateTime(DateTime.UtcNow),
                        FlightDate = Timestamp.FromDateTime(FlightDate),
                        Status = FlightStatus.Completed,
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
            Calls.Enqueue("flight.GetAvailableSeats");
            var result = new GetAvailableSeatsResult();
            result.SeatDtos.Add(
                new SeatDtoResponse
                {
                    Id = Guid.NewGuid().ToString(),
                    FlightId = request.FlightId,
                    SeatNumber = "33F",
                    Type = SeatType.Aisle,
                    Class = SeatClass.Economy,
                }
            );
            return Task.FromResult(result);
        }

        public override Task<ReserveSeatResult> ReserveSeat(ReserveSeatRequest request, ServerCallContext context)
        {
            Calls.Enqueue("flight.ReserveSeat");
            return Task.FromResult(new ReserveSeatResult { Id = Guid.NewGuid().ToString() });
        }
    }

    private sealed class FakePassengerService : PassengerGrpcService.PassengerGrpcServiceBase
    {
        public static ConcurrentQueue<string> Calls { get; set; } = new();

        public override Task<GetPassengerByIdResult> GetById(
            Contracts.Grpc.Passenger.GetByIdRequest request,
            ServerCallContext context
        )
        {
            Calls.Enqueue("passenger.GetById");
            return Task.FromResult(
                new GetPassengerByIdResult
                {
                    PassengerDto = new PassengerResponse
                    {
                        Id = request.Id,
                        Name = "Host test passenger",
                        PassportNumber = "AB123456",
                        PassengerType = PassengerType.Male,
                        Age = 30,
                        Email = "host-test@example.test",
                    },
                }
            );
        }
    }
}

[CollectionDefinition(Name)]
public sealed class BookingHostCollection : ICollectionFixture<BookingHostFixture>
{
    public const string Name = "Booking host";
}
