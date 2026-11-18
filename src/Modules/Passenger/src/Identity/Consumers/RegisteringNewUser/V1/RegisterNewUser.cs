namespace Passenger.Identity.Consumers.RegisteringNewUser.V1;

using Ardalis.GuardClauses;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.Web;
using Data;
using Humanizer;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Passenger.Passengers.Exceptions;
using Passengers.Models;
using Passengers.ValueObjects;

public class RegisterNewUserHandler : IConsumer<UserCreated>
{
    private readonly PassengerDbContext _passengerDbContext;
    private readonly IEventDispatcher _eventDispatcher;
    private readonly ILogger<RegisterNewUserHandler> _logger;
    private readonly AppOptions _options;

    public RegisterNewUserHandler(
        PassengerDbContext passengerDbContext,
        IEventDispatcher eventDispatcher,
        ILogger<RegisterNewUserHandler> logger,
        IOptions<AppOptions> options
    )
    {
        _passengerDbContext = passengerDbContext;
        _eventDispatcher = eventDispatcher;
        _logger = logger;
        _options = options.Value;
    }

    public async Task Consume(ConsumeContext<UserCreated> context)
    {
        Guard.Against.Null(context.Message, nameof(UserCreated));

        _logger.LogInformation(
            "consumer for {MessageType} in {ModuleName}",
            nameof(UserCreated).Underscore(),
            _options.Name
        );

        var message = context.Message;
        var passportNumber = message.PassportNumber.Trim().ToUpperInvariant();
        var existing = await _passengerDbContext
            .Passengers.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.PassportNumber.Value == passportNumber, context.CancellationToken);

        if (existing is not null)
        {
            if (existing.UserId == message.Id)
            {
                _logger.LogInformation(
                    "passenger {PassengerId} already exists for user {UserId}; ignoring duplicate UserCreated",
                    existing.Id,
                    message.Id
                );
                return;
            }

            _logger.LogError(
                "rejected UserCreated for user {UserId}: passport number already linked to passenger {PassengerId} owned by user {OwnerUserId}",
                message.Id,
                existing.Id,
                existing.UserId
            );
            throw new PassportNumberAlreadyLinkedException();
        }

        var passenger = Passenger.Create(
            PassengerId.Of(NewId.NextGuid()),
            Name.Of(message.Name),
            PassportNumber.Of(passportNumber),
            message.Id
        );

        await _passengerDbContext.AddAsync(passenger, context.CancellationToken);

        await _passengerDbContext.SaveChangesAsync(context.CancellationToken);

        await _eventDispatcher.SendAsync(
            new PassengerCreatedDomainEvent(passenger.Id, passenger.Name, passenger.PassportNumber),
            typeof(IInternalCommand)
        );
    }
}
