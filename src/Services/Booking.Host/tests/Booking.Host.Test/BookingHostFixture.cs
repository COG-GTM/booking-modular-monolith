using Booking.Data;
using BuildingBlocks.TestBase;
using Contracts.Grpc.Flight;
using Contracts.Grpc.Passenger;
using Xunit;

namespace Booking.Host.Test;

// Boots Booking.Host (Testcontainers EventStoreDB/Mongo/Postgres/RabbitMQ) with its Flight/Passenger gRPC
// endpoints pointed at fake services running in their own Kestrel servers.
public sealed class BookingHostFixture : TestReadFixture<Program, BookingReadDbContext>, IAsyncLifetime
{
    private readonly FakeGrpcServer _flightServer;
    private readonly FakeGrpcServer _passengerServer;

    public BookingHostFixture()
    {
        _flightServer = FakeGrpcServer
            .StartAsync(Flight, FlightGrpcService.Descriptor.FullName)
            .GetAwaiter()
            .GetResult();
        _passengerServer = FakeGrpcServer
            .StartAsync(Passenger, PassengerGrpcService.Descriptor.FullName)
            .GetAwaiter()
            .GetResult();
    }

    public FakeFlightGrpcService Flight { get; } = new();

    public FakePassengerGrpcService Passenger { get; } = new();

    public string FlightAddress => _flightServer.Address;

    public string PassengerAddress => _passengerServer.Address;

    // Applied via UseSetting, so Program sees them when it registers the gRPC clients.
    protected override IEnumerable<KeyValuePair<string, string?>> AdditionalAppSettings() =>
        [new("Grpc:FlightAddress", _flightServer.Address), new("Grpc:PassengerAddress", _passengerServer.Address)];

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await _flightServer.DisposeAsync();
        await _passengerServer.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public class BookingHostCollection : ICollectionFixture<BookingHostFixture>
{
    public const string Name = "Booking Host";
}

[Collection(BookingHostCollection.Name)]
public abstract class BookingHostTestBase : TestReadBase<Program, BookingReadDbContext>
{
    protected BookingHostTestBase(BookingHostFixture fixture)
        : base(fixture)
    {
        Host = fixture;
    }

    protected BookingHostFixture Host { get; }
}
