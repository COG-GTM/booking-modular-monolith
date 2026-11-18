using System.Security.Claims;

namespace Passenger.Passengers.Features.CompletingRegisterPassenger.V1;

using Ardalis.GuardClauses;
using BuildingBlocks.Core.CQRS;
using BuildingBlocks.Core.Event;
using BuildingBlocks.Web;
using Data;
using Dtos;
using Duende.IdentityServer.EntityFramework.Entities;
using FluentValidation;
using Mapster;
using MapsterMapper;
using MassTransit;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Passenger.Exceptions;
using Passenger.Passengers.ValueObjects;

public record CompleteRegisterPassenger(string PassportNumber, Enums.PassengerType PassengerType, int Age)
    : ICommand<CompleteRegisterPassengerResult>,
        IInternalCommand
{
    public Guid Id { get; init; } = NewId.NextGuid();
    public Guid? UserId { get; init; }
}

public record PassengerRegistrationCompletedDomainEvent(
    Guid Id,
    string Name,
    string PassportNumber,
    Enums.PassengerType PassengerType,
    int Age,
    bool IsDeleted = false
) : IDomainEvent;

public record CompleteRegisterPassengerResult(PassengerDto PassengerDto);

public record CompleteRegisterPassengerRequestDto(string PassportNumber, Enums.PassengerType PassengerType, int Age);

public record CompleteRegisterPassengerResponseDto(PassengerDto PassengerDto);

public class CompleteRegisterPassengerEndpoint : IMinimalEndpoint
{
    public IEndpointRouteBuilder MapEndpoint(IEndpointRouteBuilder builder)
    {
        builder
            .MapPost(
                $"{EndpointConfig.BaseApiPath}/passenger/complete-registration",
                async (
                    CompleteRegisterPassengerRequestDto request,
                    IMapper mapper,
                    IMediator mediator,
                    CancellationToken cancellationToken,
                    IHttpContextAccessor httpContextAccessor
                ) =>
                {
                    var userIdClaim =
                        httpContextAccessor.HttpContext?.User.FindFirst("sub")?.Value
                        ?? httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                    var command = mapper.Map<CompleteRegisterPassenger>(request) with
                    {
                        UserId = Guid.TryParse(userIdClaim, out var userId) ? userId : null,
                    };

                    var result = await mediator.Send(command, cancellationToken);

                    var response = result.Adapt<CompleteRegisterPassengerResponseDto>();

                    return Results.Ok(response);
                }
            )
            .RequireAuthorization(nameof(ApiScope))
            .WithName("CompleteRegisterPassenger")
            .WithApiVersionSet(builder.NewApiVersionSet("Passenger").Build())
            .Produces<CompleteRegisterPassengerResponseDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithSummary("Complete Register Passenger")
            .WithDescription("Complete Register Passenger")
            .WithOpenApi()
            .HasApiVersion(1.0);

        return builder;
    }
}

public class CompleteRegisterPassengerValidator : AbstractValidator<CompleteRegisterPassenger>
{
    public CompleteRegisterPassengerValidator()
    {
        RuleFor(x => x.PassportNumber).NotNull().WithMessage("The PassportNumber is required!");
        RuleFor(x => x.Age).GreaterThan(0).WithMessage("The Age must be greater than 0!");
        RuleFor(x => x.PassengerType)
            .Must(p =>
                p.GetType().IsEnum && p == Enums.PassengerType.Baby
                || p == Enums.PassengerType.Female
                || p == Enums.PassengerType.Male
                || p == Enums.PassengerType.Unknown
            )
            .WithMessage("PassengerType must be Male, Female, Baby or Unknown");
    }
}

internal class CompleteRegisterPassengerCommandHandler
    : ICommandHandler<CompleteRegisterPassenger, CompleteRegisterPassengerResult>
{
    private readonly IMapper _mapper;
    private readonly PassengerDbContext _passengerDbContext;
    private readonly ILogger<CompleteRegisterPassengerCommandHandler> _logger;

    public CompleteRegisterPassengerCommandHandler(
        IMapper mapper,
        PassengerDbContext passengerDbContext,
        ILogger<CompleteRegisterPassengerCommandHandler> logger
    )
    {
        _mapper = mapper;
        _passengerDbContext = passengerDbContext;
        _logger = logger;
    }

    public async Task<CompleteRegisterPassengerResult> Handle(
        CompleteRegisterPassenger request,
        CancellationToken cancellationToken
    )
    {
        Guard.Against.Null(request, nameof(request));

        var passenger = await _passengerDbContext.Passengers.SingleOrDefaultAsync(
            x => x.PassportNumber.Value == request.PassportNumber,
            cancellationToken
        );

        if (passenger is null)
        {
            throw new PassengerNotExist();
        }

        if (passenger.UserId is null)
        {
            _logger.LogWarning(
                "Allowing legacy passenger {PassengerId} registration without recorded ownership",
                passenger.Id.Value
            );
        }
        else if (passenger.UserId != request.UserId)
        {
            throw new PassengerNotExist();
        }

        passenger.CompleteRegistrationPassenger(
            passenger.Id,
            passenger.Name,
            passenger.PassportNumber,
            request.PassengerType,
            Age.Of(request.Age)
        );

        var updatePassenger = _passengerDbContext.Passengers.Update(passenger).Entity;

        var passengerDto = _mapper.Map<PassengerDto>(updatePassenger);

        return new CompleteRegisterPassengerResult(passengerDto);
    }
}
