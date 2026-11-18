using BuildingBlocks.Core.CQRS;
using FluentAssertions;
using Mapster;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Integration.Test.Passenger.Features;

using PassengerFeature = global::Passenger.Passengers.Features.CompletingRegisterPassenger.V1;
using PassengerModule = global::Passenger;

public class CompleteRegisterPassengerOwnershipTests
{
    private readonly IMapper _mapper = CreateMapper();

    [Fact]
    public async Task Owner_can_complete_registration_and_persist_updates()
    {
        var databaseName = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid();
        var passenger = CreatePassenger(userId);
        await SeedAsync(databaseName, passenger);

        await using (var context = CreateContext(databaseName))
        {
            var handler = CreateHandler(context);
            var result = await handler.Handle(
                new PassengerFeature.CompleteRegisterPassenger(
                    passenger.PassportNumber.Value,
                    PassengerModule.Passengers.Enums.PassengerType.Female,
                    36
                )
                {
                    UserId = userId,
                },
                CancellationToken.None
            );

            result.PassengerDto.Age.Should().Be(36);
            result.PassengerDto.PassengerType.Should().Be(PassengerModule.Passengers.Enums.PassengerType.Female);
            await context.SaveChangesAsync();
        }

        await using var freshContext = CreateContext(databaseName);
        var updatedPassenger = await freshContext.Passengers.SingleAsync(x =>
            x.PassportNumber.Value == passenger.PassportNumber.Value
        );
        updatedPassenger.Age!.Value.Should().Be(36);
        updatedPassenger.PassengerType.Should().Be(PassengerModule.Passengers.Enums.PassengerType.Female);
    }

    [Fact]
    public async Task Other_users_passport_is_not_found_and_remains_unchanged()
    {
        var databaseName = Guid.NewGuid().ToString();
        var passenger = CreatePassenger(Guid.NewGuid());
        await SeedAsync(databaseName, passenger);

        await using (var context = CreateContext(databaseName))
        {
            var handler = CreateHandler(context);
            var act = () =>
                handler.Handle(
                    new PassengerFeature.CompleteRegisterPassenger(
                        passenger.PassportNumber.Value,
                        PassengerModule.Passengers.Enums.PassengerType.Male,
                        38
                    )
                    {
                        UserId = Guid.NewGuid(),
                    },
                    CancellationToken.None
                );

            await act.Should().ThrowAsync<PassengerModule.Exceptions.PassengerNotExist>();
        }

        await using var freshContext = CreateContext(databaseName);
        var unchangedPassenger = await freshContext.Passengers.SingleAsync();
        unchangedPassenger.Age.Should().BeNull();
        unchangedPassenger.PassengerType.Should().Be(PassengerModule.Passengers.Enums.PassengerType.Unknown);
    }

    [Fact]
    public async Task Nonexistent_passport_is_not_found()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var context = CreateContext(databaseName);
        var handler = CreateHandler(context);
        var act = () =>
            handler.Handle(
                new PassengerFeature.CompleteRegisterPassenger(
                    "missing-passport",
                    PassengerModule.Passengers.Enums.PassengerType.Male,
                    38
                )
                {
                    UserId = Guid.NewGuid(),
                },
                CancellationToken.None
            );

        await act.Should().ThrowAsync<PassengerModule.Exceptions.PassengerNotExist>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("11111111-1111-1111-1111-111111111111")]
    public async Task Legacy_passenger_can_complete_registration(string? callerId)
    {
        var databaseName = Guid.NewGuid().ToString();
        var passenger = CreatePassenger(null);
        await SeedAsync(databaseName, passenger);

        await using (var context = CreateContext(databaseName))
        {
            var handler = CreateHandler(context);
            await handler.Handle(
                new PassengerFeature.CompleteRegisterPassenger(
                    passenger.PassportNumber.Value,
                    PassengerModule.Passengers.Enums.PassengerType.Male,
                    38
                )
                {
                    UserId = callerId is null ? null : Guid.Parse(callerId),
                },
                CancellationToken.None
            );

            await context.SaveChangesAsync();
        }

        await using var freshContext = CreateContext(databaseName);
        var updatedPassenger = await freshContext.Passengers.SingleAsync();
        updatedPassenger.UserId.Should().BeNull();
        updatedPassenger.Age!.Value.Should().Be(38);
        updatedPassenger.PassengerType.Should().Be(PassengerModule.Passengers.Enums.PassengerType.Male);
    }

    [Fact]
    public void Validator_allows_missing_user_id_for_legacy_passengers()
    {
        var validator = new PassengerFeature.CompleteRegisterPassengerValidator();
        var emptyUserIdResult = validator.Validate(
            new PassengerFeature.CompleteRegisterPassenger(
                "P-123",
                PassengerModule.Passengers.Enums.PassengerType.Male,
                36
            )
        );
        var populatedUserIdResult = validator.Validate(
            new PassengerFeature.CompleteRegisterPassenger(
                "P-123",
                PassengerModule.Passengers.Enums.PassengerType.Male,
                36
            )
            {
                UserId = Guid.NewGuid(),
            }
        );

        emptyUserIdResult.IsValid.Should().BeTrue();
        populatedUserIdResult.Errors.Should().NotContain(error => error.PropertyName == "UserId");
    }

    [Fact]
    public void Passenger_create_stores_the_given_user_id()
    {
        var userId = Guid.NewGuid();

        var passenger = CreatePassenger(userId);

        passenger.UserId.Should().Be(userId);
    }

    [Fact]
    public async Task Owned_passenger_without_caller_id_is_not_found()
    {
        var databaseName = Guid.NewGuid().ToString();
        var passenger = CreatePassenger(Guid.NewGuid());
        await SeedAsync(databaseName, passenger);
        await using var context = CreateContext(databaseName);
        var act = () =>
            CreateHandler(context)
                .Handle(
                    new PassengerFeature.CompleteRegisterPassenger(
                        passenger.PassportNumber.Value,
                        PassengerModule.Passengers.Enums.PassengerType.Male,
                        38
                    ),
                    CancellationToken.None
                );

        await act.Should().ThrowAsync<PassengerModule.Exceptions.PassengerNotExist>();
        (await context.Passengers.SingleAsync()).Age.Should().BeNull();
    }

    private PassengerFeature.CompleteRegisterPassengerCommandHandler CreateHandler(
        PassengerModule.Data.PassengerDbContext context
    ) => new(_mapper, context, NullLogger<PassengerFeature.CompleteRegisterPassengerCommandHandler>.Instance);

    private static PassengerModule.Passengers.Models.Passenger CreatePassenger(Guid? userId) =>
        PassengerModule.Passengers.Models.Passenger.Create(
            PassengerModule.Passengers.ValueObjects.PassengerId.Of(Guid.NewGuid()),
            PassengerModule.Passengers.ValueObjects.Name.Of("Taylor"),
            PassengerModule.Passengers.ValueObjects.PassportNumber.Of("P-123"),
            userId: userId
        );

    private static PassengerModule.Data.PassengerDbContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<PassengerModule.Data.PassengerDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new PassengerModule.Data.PassengerDbContext(options);
    }

    private static async Task SeedAsync(string databaseName, PassengerModule.Passengers.Models.Passenger passenger)
    {
        await using var context = CreateContext(databaseName);
        context.Passengers.Add(passenger);
        await context.SaveChangesAsync();
    }

    private static IMapper CreateMapper()
    {
        var config = TypeAdapterConfig.GlobalSettings;
        config.Scan(typeof(PassengerModule.Passengers.Features.PassengerMappings).Assembly);
        return new Mapper(config);
    }
}
