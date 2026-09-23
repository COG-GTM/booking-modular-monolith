using System;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.TestBase;
using Flight.Data;
using FluentAssertions;
using Integration.Test.Fakes;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Xunit;

namespace Integration.Test.Seat.Features;

using global::Flight.Seats.Exceptions;

public class CreateSeatMongoTests : FlightIntegrationTestBase
{
    public CreateSeatMongoTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFactory)
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_insert_seat_read_model_to_mongo()
    {
        // Arrange
        var flightCommand = new FakeCreateFlightMongoCommand().Generate();

        await Fixture.SendAsync(flightCommand);

        var seatCommand = new FakeCreateSeatMongoCommand(flightCommand.Id).Generate();

        // Act
        await Fixture.SendAsync(seatCommand);

        // Assert
        var seatReadModel = await Fixture.ExecuteReadContextAsync(db =>
            db.Seat.AsQueryable().SingleOrDefaultAsync(x => x.SeatId == seatCommand.Id)
        );

        seatReadModel.Should().NotBeNull();
        seatReadModel!.Id.Should().NotBe(Guid.Empty);
        seatReadModel.Id.Should().NotBe(seatCommand.Id);
        seatReadModel.SeatNumber.Should().Be(seatCommand.SeatNumber);
        seatReadModel.Type.Should().Be(seatCommand.Type);
        seatReadModel.Class.Should().Be(seatCommand.Class);
        seatReadModel.FlightId.Should().Be(flightCommand.Id);
        seatReadModel.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task should_throw_seat_already_exist_exception_when_seat_read_model_exists()
    {
        // Arrange
        var flightCommand = new FakeCreateFlightMongoCommand().Generate();

        await Fixture.SendAsync(flightCommand);

        var seatCommand = new FakeCreateSeatMongoCommand(flightCommand.Id).Generate();

        await Fixture.SendAsync(seatCommand);

        // Act
        Func<Task> act = async () => await Fixture.SendAsync(seatCommand);

        // Assert
        await act.Should().ThrowAsync<SeatAlreadyExistException>();

        var count = await Fixture.ExecuteReadContextAsync(db =>
            db.Seat.CountDocumentsAsync(x => x.SeatId == seatCommand.Id)
        );

        count.Should().Be(1);
    }

    [Fact]
    public async Task should_insert_seat_read_model_when_existing_one_is_reserved()
    {
        // Arrange
        var flightCommand = new FakeCreateFlightMongoCommand().Generate();

        await Fixture.SendAsync(flightCommand);

        var seatCommand = new FakeCreateSeatMongoCommand(flightCommand.Id).Generate();

        await Fixture.SendAsync(seatCommand);
        await Fixture.SendAsync(new FakeReserveSeatMongoCommand(seatCommand).Generate());

        // Act
        await Fixture.SendAsync(seatCommand);

        // Assert
        var seatReadModels = await Fixture.ExecuteReadContextAsync(db =>
            db.Seat.AsQueryable().Where(x => x.SeatId == seatCommand.Id).ToListAsync()
        );

        seatReadModels.Should().HaveCount(2);
        seatReadModels.Should().ContainSingle(x => !x.IsDeleted);
    }
}
