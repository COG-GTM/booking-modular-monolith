using Booking.Data;
using Booking.Host;
using BuildingBlocks.TestBase;
using Xunit;

namespace Host.Test.Integration;

public sealed class BookingHostIntegrationFixture : TestReadFixture<Program, BookingReadDbContext>, IAsyncLifetime
{
    private readonly string? _previousFlightAddress;
    private readonly string? _previousPassengerAddress;
    private readonly FakeFlightGrpcServer _flightServer;
    private readonly FakePassengerGrpcServer _passengerServer;

    public BookingHostIntegrationFixture()
    {
        _flightServer = FakeFlightGrpcServer.StartAsync().GetAwaiter().GetResult();
        _passengerServer = FakePassengerGrpcServer.StartAsync().GetAwaiter().GetResult();

        _previousFlightAddress = Environment.GetEnvironmentVariable("Services__flight__grpc__0");
        _previousPassengerAddress = Environment.GetEnvironmentVariable("Services__passenger__grpc__0");
        Environment.SetEnvironmentVariable("Services__flight__grpc__0", _flightServer.Address);
        Environment.SetEnvironmentVariable("Services__passenger__grpc__0", _passengerServer.Address);
    }

    public FakeFlightGrpcServer FlightServer => _flightServer;
    public FakePassengerGrpcServer PassengerServer => _passengerServer;

    async Task IAsyncLifetime.InitializeAsync()
    {
        await base.InitializeAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        try
        {
            await base.DisposeAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable("Services__flight__grpc__0", _previousFlightAddress);
            Environment.SetEnvironmentVariable("Services__passenger__grpc__0", _previousPassengerAddress);
            await _flightServer.DisposeAsync();
            await _passengerServer.DisposeAsync();
        }
    }
}

[CollectionDefinition(Name)]
public sealed class BookingHostIntegrationCollection : ICollectionFixture<BookingHostIntegrationFixture>
{
    public const string Name = "Booking.Host integration";
}
