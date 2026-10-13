namespace Passenger.Passengers.Features.GettingPassengerById.V1;

using System.Security.Claims;
using Ardalis.GuardClauses;
using BuildingBlocks.Constants;
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
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Passenger.Data;
using Passenger.Passengers.Dtos;
using Passenger.Passengers.Exceptions;

public record GetPassengerById(Guid Id) : IQuery<GetPassengerByIdResult>;

public record GetPassengerByIdResult(PassengerDto PassengerDto, Guid UserId);

public record GetPassengerByIdResponseDto(PassengerDto PassengerDto);

public class GetPassengerByIdEndpoint : IMinimalEndpoint
{
    public IEndpointRouteBuilder MapEndpoint(IEndpointRouteBuilder builder)
    {
        builder.MapGet($"{EndpointConfig.BaseApiPath}/passenger/{{id}}",
                async (Guid id, ClaimsPrincipal user, IMediator mediator, CancellationToken cancellationToken) =>
                {
                    var result = await mediator.Send(new GetPassengerById(id), cancellationToken);

                    if (!PassengerAccessPolicy.CanRead(user, result.UserId))
                    {
                        return Results.Forbid();
                    }

                    var response = result.Adapt<GetPassengerByIdResponseDto>();

                    return Results.Ok(response);
                })
            .RequireAuthorization(nameof(ApiScope))
            .WithName("GetPassengerById")
            .WithApiVersionSet(builder.NewApiVersionSet("Passenger").Build())
            .Produces<GetPassengerByIdResponseDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Get Passenger By Id")
            .WithDescription("Get Passenger By Id")
            .WithOpenApi()
            .HasApiVersion(1.0);

        return builder;
    }
}

public static class PassengerAccessPolicy
{
    // Only the user that owns the passenger record, or an admin, may read it.
    public static bool CanRead(ClaimsPrincipal user, Guid ownerUserId)
    {
        if (user.IsInRole(IdentityConstant.Role.Admin))
        {
            return true;
        }

        var subject = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(subject, out var currentUserId)
               && currentUserId != Guid.Empty
               && currentUserId == ownerUserId;
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

        var passenger =
            await _passengerReadDbContext.Passenger.AsQueryable()
                .SingleOrDefaultAsync(x => x.PassengerId == query.Id && x.IsDeleted == false, cancellationToken);

        if (passenger is null)
        {
            throw new PassengerNotFoundException();
        }

        var passengerDto = _mapper.Map<PassengerDto>(passenger);

        return new GetPassengerByIdResult(passengerDto, passenger.UserId);
    }
}