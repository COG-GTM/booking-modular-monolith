namespace Unit.Test.Flight.Features.Handlers.GetFlightById;

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using global::Flight.Flights.Features.GettingFlightById.V1;
using Unit.Test.Common;
using Xunit;

[Collection(nameof(UnitTestFixture))]
public class GetFlightByIdQueryHandlerTests
{
    private readonly UnitTestFixture _fixture;

    public GetFlightByIdQueryHandlerTests(UnitTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task handler_with_null_query_should_throw_argument_exception()
    {
        // Arrange
        var context = MockFlightReadDbContext.Create();
        var handler = new GetFlightByIdHandler(_fixture.Mapper, context);
        GetFlightById query = null;

        // Act
        Func<Task> act = async () => { await handler.Handle(query, CancellationToken.None); };

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
