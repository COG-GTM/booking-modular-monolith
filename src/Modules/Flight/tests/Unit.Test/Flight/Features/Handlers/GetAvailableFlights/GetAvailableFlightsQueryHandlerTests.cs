namespace Unit.Test.Flight.Features.Handlers.GetAvailableFlights;

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using global::Flight.Flights.Features.GettingAvailableFlights.V1;
using Unit.Test.Common;
using Xunit;

[Collection(nameof(UnitTestFixture))]
public class GetAvailableFlightsQueryHandlerTests
{
    private readonly UnitTestFixture _fixture;

    public GetAvailableFlightsQueryHandlerTests(UnitTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task handler_with_null_query_should_throw_argument_exception()
    {
        // Arrange
        var context = MockFlightReadDbContext.Create();
        var handler = new GetAvailableFlightsHandler(_fixture.Mapper, context);
        GetAvailableFlights query = null;

        // Act
        Func<Task> act = async () => { await handler.Handle(query, CancellationToken.None); };

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
