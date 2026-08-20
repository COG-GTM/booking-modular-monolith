using Booking.Data;
using BuildingBlocks.TestBase;
using Xunit;

namespace BookingService.Integration.Test;

[Collection(BookingServiceIntegrationTestCollection.Name)]
public class BookingServiceIntegrationTestBase : TestReadBase<Program, BookingReadDbContext>
{
    public BookingServiceIntegrationTestBase(TestReadFixture<Program, BookingReadDbContext> integrationTestFixture) : base(integrationTestFixture)
    {
    }
}

[CollectionDefinition(Name)]
public class BookingServiceIntegrationTestCollection : ICollectionFixture<TestReadFixture<Program, BookingReadDbContext>>
{
    public const string Name = "Booking Service Integration Test";
}
