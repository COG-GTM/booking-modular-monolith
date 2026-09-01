namespace Unit.Test.Seat.Features;

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using global::Flight.Seats.Features.GettingAvailableSeats.V1;
using Unit.Test.Common;
using Xunit;

[Collection(nameof(UnitTestFixture))]
public class GetAvailableSeatsQueryHandlerTests
{
    private readonly UnitTestFixture _fixture;

    public GetAvailableSeatsQueryHandlerTests(UnitTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task handler_with_null_query_should_throw_argument_exception()
    {
        // Arrange
        var context = MockFlightReadDbContext.Create();
        var handler = new GetAvailableSeatsQueryHandler(_fixture.Mapper, context);
        GetAvailableSeats query = null;

        // Act
        Func<Task> act = async () => { await handler.Handle(query, CancellationToken.None); };

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
