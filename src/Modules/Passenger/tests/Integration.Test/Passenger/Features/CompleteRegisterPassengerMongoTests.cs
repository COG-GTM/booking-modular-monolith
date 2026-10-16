using BuildingBlocks.TestBase;
using FluentAssertions;
using Integration.Test.Fakes;
using MongoDB.Driver;
using Passenger;
using Passenger.Data;
using Passenger.Host;
using Passenger.Passengers.Enums;
using Passenger.Passengers.Exceptions;
using Passenger.Passengers.Features.CompletingRegisterPassenger.V1;
using Passenger.Passengers.Features.GettingPassengerById.V1;
using Passenger.Passengers.Models;
using Xunit;

namespace Integration.Test.Passenger.Features;

public class CompleteRegisterPassengerMongoTests : PassengerIntegrationTestBase
{
    public CompleteRegisterPassengerMongoTests(
        TestFixture<Program, PassengerDbContext, PassengerReadDbContext> integrationTestFactory
    )
        : base(integrationTestFactory) { }

    [Fact]
    public async Task Mongo_handler_inserts_then_updates_one_read_model_for_the_same_passenger()
    {
        var id = Guid.NewGuid();
        var first = new FakeCompleteRegisterPassengerMongoCommand().Generate() with { Id = id };

        await Fixture.SendAsync(first);

        var updated = first with
        {
            Name = "Updated Passenger",
            PassportNumber = "987654321",
            PassengerType = PassengerType.Female,
            Age = 41,
        };
        await Fixture.SendAsync(updated);

        var documents = await Fixture.ExecuteReadContextAsync(db =>
            db.Passenger.Find(passenger => passenger.PassengerId == id).ToListAsync()
        );

        documents.Should().ContainSingle();
        documents[0].PassengerId.Should().Be(id);
        documents[0].Name.Should().Be("Updated Passenger");
        documents[0].PassportNumber.Should().Be("987654321");
        documents[0].PassengerType.Should().Be(PassengerType.Female);
        documents[0].Age.Should().Be(41);
        documents[0].IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Get_by_id_throws_for_an_unknown_passenger()
    {
        var query = new GetPassengerById(Guid.NewGuid());

        await FluentActions.Invoking(() => Fixture.SendAsync(query)).Should().ThrowAsync<PassengerNotFoundException>();
    }

    [Fact]
    public async Task Get_by_id_does_not_return_a_soft_deleted_read_model()
    {
        var id = Guid.NewGuid();
        var deleted = new PassengerReadModel
        {
            Id = Guid.NewGuid(),
            PassengerId = id,
            Name = "Deleted Passenger",
            PassportNumber = "123456789",
            PassengerType = PassengerType.Male,
            Age = 30,
            IsDeleted = true,
        };
        await Fixture.InsertMongoDbContextAsync("passenger", deleted);

        await FluentActions
            .Invoking(() => Fixture.SendAsync(new GetPassengerById(id)))
            .Should()
            .ThrowAsync<PassengerNotFoundException>();
    }
}
