using Booking.Api;
using Booking.Data;
using BuildingBlocks.TestBase;
using Xunit;

namespace Booking.Api.Test;

[Collection(BookingApiTestCollection.Name)]
public class BookingApiTestBase : TestReadBase<Program, BookingReadDbContext>
{
    public BookingApiTestBase(TestReadFixture<Program, BookingReadDbContext> integrationTestFixture)
        : base(integrationTestFixture) { }
}

[CollectionDefinition(Name)]
public class BookingApiTestCollection : ICollectionFixture<TestReadFixture<Program, BookingReadDbContext>>
{
    public const string Name = "Booking Service Smoke Test";
}
