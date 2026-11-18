using System.Net;
using BuildingBlocks.Exception;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

namespace Unit.Test.Seat.Features;

using global::Flight.Seats.Exceptions;
using global::Flight.Seats.Features.CreatingSeat.V1;
using global::Flight.Seats.ValueObjects;

public class CreateSeatUniquenessTests
{
    [Fact]
    public async Task handler_with_soft_deleted_seat_should_throw_seat_already_exist_exception()
    {
        using var fixture = new UnitTestFixture();
        var existingSeat = fixture.DbContext.Seats.First();
        existingSeat.IsDeleted = true;
        await fixture.DbContext.SaveChangesAsync();
        fixture.DbContext.ChangeTracker.Clear();
        (await fixture.DbContext.Seats.AnyAsync(x => x.Id == existingSeat.Id)).Should().BeFalse();
        (await fixture.DbContext.Seats.IgnoreQueryFilters().SingleAsync(x => x.Id == existingSeat.Id))
            .IsDeleted.Should()
            .BeTrue();
        var command = new FakeCreateSeatCommand().Generate() with
        {
            SeatNumber = existingSeat.SeatNumber.Value,
            FlightId = existingSeat.FlightId.Value,
        };
        var handler = new CreateSeatCommandHandler(fixture.DbContext);

        var act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<SeatAlreadyExistException>();
    }

    [Fact]
    public async Task handler_with_same_seat_number_on_another_flight_should_create_seat()
    {
        using var fixture = new UnitTestFixture();
        var existingSeat = fixture.DbContext.Seats.First();
        var command = new FakeCreateSeatCommand().Generate() with { SeatNumber = existingSeat.SeatNumber.Value };
        command.FlightId.Should().NotBe(existingSeat.FlightId.Value);
        var handler = new CreateSeatCommandHandler(fixture.DbContext);

        var response = await handler.Handle(command, CancellationToken.None);
        await fixture.DbContext.SaveChangesAsync();

        var entity = await fixture.DbContext.Seats.FindAsync(SeatId.Of(response.Id));
        entity.Should().NotBeNull();
        entity!.SeatNumber.Value.Should().Be(existingSeat.SeatNumber.Value);
        entity.FlightId.Value.Should().Be(command.FlightId);
        response.Id.Should().Be(command.Id);
    }

    [Fact]
    public void seat_already_exist_exception_should_be_conflict_with_status_code_409()
    {
        var exception = new SeatAlreadyExistException();

        exception.Should().BeAssignableTo<ConflictException>();
        exception.StatusCode.Should().Be(HttpStatusCode.Conflict);
        ((int)exception.StatusCode).Should().Be(409);
    }
}
