using BookingFlight;
using BookingPassenger;
using BuildingBlocks.Core;
using BuildingBlocks.EventStoreDB.Repository;
using BuildingBlocks.Web;
using MapsterMapper;
using NSubstitute;
using Xunit;

namespace Unit.Test.Common;

[CollectionDefinition(nameof(UnitTestFixture))]
public class FixtureCollection : ICollectionFixture<UnitTestFixture> { }

public class UnitTestFixture : IDisposable
{
    public UnitTestFixture()
    {
        EventStoreDbRepository =
            Substitute.For<IEventStoreDBRepository<global::Booking.Booking.Models.Booking>>();
        EventDispatcher = Substitute.For<IEventDispatcher>();
        FlightGrpcServiceClient = Substitute.For<FlightGrpcService.FlightGrpcServiceClient>();
        PassengerGrpcServiceClient = Substitute.For<PassengerGrpcService.PassengerGrpcServiceClient>();
        Mapper = Substitute.For<IMapper>();
        CurrentUserProvider = Substitute.For<ICurrentUserProvider>();
    }

    public IEventStoreDBRepository<global::Booking.Booking.Models.Booking> EventStoreDbRepository { get; }
    public IEventDispatcher EventDispatcher { get; }
    public FlightGrpcService.FlightGrpcServiceClient FlightGrpcServiceClient { get; }
    public PassengerGrpcService.PassengerGrpcServiceClient PassengerGrpcServiceClient { get; }
    public IMapper Mapper { get; }
    public ICurrentUserProvider CurrentUserProvider { get; }

    public void Dispose() { }
}
