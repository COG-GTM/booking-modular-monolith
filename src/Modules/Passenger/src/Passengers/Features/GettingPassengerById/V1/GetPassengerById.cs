namespace Passenger.Passengers.Features.GettingPassengerById.V1;

using System.Security.Claims;
using Ardalis.GuardClauses;
using BuildingBlocks.Core.CQRS;
using BuildingBlocks.Web;
using Duende.IdentityServer.EntityFramework.Entities;
using FluentValidation;
using Mapster;
using MapsterMapper;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Passenger.Data;
using Passenger.Passengers.Dtos;
using Passenger.Passengers.Exceptions;
using Passenger.Passengers.ValueObjects;

public record GetPassengerById(Guid Id) : IQuery<GetPassengerByIdResult>;

public record GetPassengerByIdResult(PassengerDto PassengerDto);

public record GetPassengerByIdResponseDto(PassengerDto PassengerDto);

public class GetPassengerByIdEndpoint : IMinimalEndpoint
{
    public IEndpointRouteBuilder MapEndpoint(IEndpointRouteBuilder builder)
    {
        builder
            .MapGet(
                $"{EndpointConfig.BaseApiPath}/passenger/{{id}}",
                async (
                    Guid id,
                    IMediator mediator,
                    HttpContext httpContext,
                    PassengerDbContext dbContext,
                    ILogger<GetPassengerByIdEndpoint> logger,
                    CancellationToken cancellationToken
                ) =>
                {
                    await EnsureOwnershipAsync(id, httpContext.User, dbContext, logger, cancellationToken);
                    var result = await mediator.Send(new GetPassengerById(id), cancellationToken);

                    var response = result.Adapt<GetPassengerByIdResponseDto>();

                    return Results.Ok(response);
                }
            )
            .RequireAuthorization(nameof(ApiScope))
            .WithName("GetPassengerById")
            .WithApiVersionSet(builder.NewApiVersionSet("Passenger").Build())
            .Produces<GetPassengerByIdResponseDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithSummary("Get Passenger By Id")
            .WithDescription("Get Passenger By Id")
            .WithOpenApi()
            .HasApiVersion(1.0);

        return builder;
    }

    internal static async Task EnsureOwnershipAsync(
        Guid id,
        ClaimsPrincipal user,
        PassengerDbContext dbContext,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        var passenger = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleOrDefaultAsync(
            dbContext.Passengers,
            x => x.Id == PassengerId.Of(id) && !x.IsDeleted,
            cancellationToken
        );
        if (passenger is null)
        {
            throw new PassengerNotFoundException();
        }

        if (passenger.UserId is null)
        {
            logger.LogWarning("Allowing legacy passenger {PassengerId} lookup without recorded ownership", id);
            return;
        }

        var subject = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(subject, out var userId) || userId != passenger.UserId)
        {
            throw new PassengerNotFoundException();
        }
    }
}

public class GetPassengerByIdValidator : AbstractValidator<GetPassengerById>
{
    public GetPassengerByIdValidator()
    {
        RuleFor(x => x.Id).NotNull().WithMessage("Id is required!");
    }
}

internal class GetPassengerByIdHandler : IQueryHandler<GetPassengerById, GetPassengerByIdResult>
{
    private readonly IMapper _mapper;
    private readonly PassengerReadDbContext _passengerReadDbContext;

    public GetPassengerByIdHandler(IMapper mapper, PassengerReadDbContext passengerReadDbContext)
    {
        _mapper = mapper;
        _passengerReadDbContext = passengerReadDbContext;
    }

    public async Task<GetPassengerByIdResult> Handle(GetPassengerById query, CancellationToken cancellationToken)
    {
        Guard.Against.Null(query, nameof(query));

        var passenger = await _passengerReadDbContext
            .Passenger.AsQueryable()
            .SingleOrDefaultAsync(x => x.PassengerId == query.Id && x.IsDeleted == false, cancellationToken);

        if (passenger is null)
        {
            throw new PassengerNotFoundException();
        }

        var passengerDto = _mapper.Map<PassengerDto>(passenger);

        return new GetPassengerByIdResult(passengerDto);
    }
}
