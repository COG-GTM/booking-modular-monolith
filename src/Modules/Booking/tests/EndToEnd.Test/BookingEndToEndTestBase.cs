using Api;
using Booking.Data;
using BuildingBlocks.TestBase;
using Xunit;

namespace EndToEnd.Test;

[Collection(EndToEndTestCollection.Name)]
public class BookingEndToEndTestBase : TestReadBase<Program, BookingReadDbContext>
{
    public BookingEndToEndTestBase(TestReadFixture<Program, BookingReadDbContext> integrationTestFixture)
        : base(integrationTestFixture) { }
}

[CollectionDefinition(Name)]
public class EndToEndTestCollection : ICollectionFixture<TestReadFixture<Program, BookingReadDbContext>>
{
    public const string Name = "Booking EndToEnd Test";
}
