using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Passenger.Passengers.Enums;
using Passenger.Passengers.Exceptions;
using Passenger.Passengers.Features.CompletingRegisterPassenger.V1;
using Passenger.Passengers.ValueObjects;
using Unit.Test.Common;
using Xunit;

namespace Unit.Test.Passengers.Features.CompletingRegisterPassenger;

public class CompleteRegisterPassengerHandlerTests
{
    [Fact]
    public async Task Handle_updates_the_existing_passenger_and_returns_the_mapped_dto()
    {
        await using var db = DbContextFactory.Create();
        var mapper = MapperFactory.Create();
        var passenger = global::Passenger.Passengers.Models.Passenger.Create(
            PassengerId.Of(Guid.NewGuid()),
            Name.Of("Sam Passenger"),
            PassportNumber.Of("123456789")
        );
        db.Passengers.Add(passenger);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var handler = new CompleteRegisterPassengerCommandHandler(mapper, db);
        var request = new CompleteRegisterPassenger("123456789", PassengerType.Female, 36);

        var result = await handler.Handle(request, CancellationToken.None);
        var trackedPassenger = db.Passengers.Local.Single();

        result
            .PassengerDto.Should()
            .Be(
                new global::Passenger.Passengers.Dtos.PassengerDto(
                    passenger.Id,
                    "Sam Passenger",
                    "123456789",
                    PassengerType.Female,
                    36
                )
            );
        db.Entry(trackedPassenger).State.Should().Be(EntityState.Modified);
        trackedPassenger.Age.Should().Be(Age.Of(36));
        trackedPassenger
            .DomainEvents.Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                new PassengerRegistrationCompletedDomainEvent(
                    trackedPassenger.Id,
                    trackedPassenger.Name,
                    trackedPassenger.PassportNumber,
                    PassengerType.Female,
                    36
                )
            );
    }

    [Fact]
    public async Task Handle_throws_when_the_passport_number_is_unknown()
    {
        await using var db = DbContextFactory.Create();
        var handler = new CompleteRegisterPassengerCommandHandler(MapperFactory.Create(), db);

        await FluentActions
            .Invoking(
                () =>
                    handler.Handle(
                        new CompleteRegisterPassenger("missing", PassengerType.Male, 30),
                        CancellationToken.None
                    )
            )
            .Should()
            .ThrowAsync<PassengerNotExist>();
    }

    [Fact]
    public async Task Handle_rejects_an_invalid_age_for_an_existing_passenger()
    {
        await using var db = DbContextFactory.Create();
        var passenger = global::Passenger.Passengers.Models.Passenger.Create(
            PassengerId.Of(Guid.NewGuid()),
            Name.Of("Sam Passenger"),
            PassportNumber.Of("123456789")
        );
        db.Passengers.Add(passenger);
        await db.SaveChangesAsync();
        var handler = new CompleteRegisterPassengerCommandHandler(MapperFactory.Create(), db);

        await FluentActions
            .Invoking(
                () =>
                    handler.Handle(
                        new CompleteRegisterPassenger("123456789", PassengerType.Male, 0),
                        CancellationToken.None
                    )
            )
            .Should()
            .ThrowAsync<global::Passenger.Passengers.Exceptions.InvalidAgeException>();
    }

    [Fact]
    public async Task Handle_rejects_a_null_request()
    {
        await using var db = DbContextFactory.Create();
        var handler = new CompleteRegisterPassengerCommandHandler(MapperFactory.Create(), db);

        await FluentActions
            .Invoking(() => handler.Handle(null!, CancellationToken.None))
            .Should()
            .ThrowAsync<ArgumentNullException>();
    }
}
