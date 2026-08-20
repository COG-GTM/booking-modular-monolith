using System.Threading.Tasks;
using BuildingBlocks.Core;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Integration.Test;

using global::Booking;
using global::Booking.Data;

[Collection(BookingApiTestCollection.Name)]
public class BookingApiHostTests : TestReadBase<Booking.Api.Program, BookingReadDbContext>
{
    public BookingApiHostTests(TestReadFixture<Booking.Api.Program, BookingReadDbContext> integrationTestFixture)
        : base(integrationTestFixture) { }

    [Fact]
    public async Task should_serve_root_endpoint_with_service_name()
    {
        var response = await Fixture.HttpClient.GetStringAsync("/");

        response.Should().Be("Booking-Service");
    }

    [Fact]
    public void should_register_booking_event_mapper_as_the_only_event_mapper()
    {
        using var scope = Fixture.ServiceProvider.CreateScope();

        var eventMappers = scope.ServiceProvider.GetServices<IEventMapper>();

        eventMappers.Should().ContainSingle().Which.Should().BeOfType<BookingEventMapper>();
    }
}

[CollectionDefinition(Name)]
public class BookingApiTestCollection : ICollectionFixture<TestReadFixture<Booking.Api.Program, BookingReadDbContext>>
{
    public const string Name = "BookingApi Integration Test";
}
