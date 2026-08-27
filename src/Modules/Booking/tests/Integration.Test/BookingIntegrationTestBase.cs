using Booking.Data;
using BookingService;
using BuildingBlocks.TestBase;
using Xunit;

namespace Integration.Test;

public class BookingServiceTestFixture : TestReadFixture<Program, BookingReadDbContext>
{
    protected override TestInfrastructure RequiredInfrastructure =>
        TestInfrastructure.PersistMessagePostgres
        | TestInfrastructure.RabbitMq
        | TestInfrastructure.Mongo
        | TestInfrastructure.EventStore;
}

[Collection(IntegrationTestCollection.Name)]
public class BookingIntegrationTestBase : TestReadBase<Program, BookingReadDbContext>
{
    public BookingIntegrationTestBase(BookingServiceTestFixture integrationTestFixture)
        : base(integrationTestFixture) { }
}

[CollectionDefinition(Name)]
public class IntegrationTestCollection : ICollectionFixture<BookingServiceTestFixture>
{
    public const string Name = "Booking Integration Test";
}
