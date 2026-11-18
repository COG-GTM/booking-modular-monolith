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

// Requester is null only for trusted in-process callers (e.g. the gRPC service); HTTP callers must pass it.
public record GetPassengerById(Guid Id, PassengerRequester? Requester = null) : IQuery<GetPassengerByIdResult>;

public record PassengerRequester(Guid? UserId, bool IsAdmin)
{
    public static PassengerRequester From(ClaimsPrincipal user)
    {
        var subject = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

        var isAdmin = user.IsInRole(IdentityConstant.Role.Admin) ||
                      user.HasClaim("role", IdentityConstant.Role.Admin);

        return new PassengerRequester(Guid.TryParse(subject, out var userId) ? userId : null, isAdmin);
    }

    public bool CanAccess(Guid? ownerUserId) =>
        IsAdmin || (UserId is not null && ownerUserId == UserId);
}

public record GetPassengerByIdResult(PassengerDto PassengerDto);

public record GetPassengerByIdResponseDto(PassengerDto PassengerDto);

public class GetPassengerByIdEndpoint : IMinimalEndpoint
{
    public IEndpointRouteBuilder MapEndpoint(IEndpointRouteBuilder builder)
    {
        builder.MapGet($"{EndpointConfig.BaseApiPath}/passenger/{{id}}",
                async (Guid id, IMediator mediator, HttpContext httpContext, CancellationToken cancellationToken) =>
                {
                    var result = await mediator.Send(
                        new GetPassengerById(id, PassengerRequester.From(httpContext.User)), cancellationToken);

                    var response = result.Adapt<GetPassengerByIdResponseDto>();

                    return Results.Ok(response);
                })
            .RequireAuthorization(nameof(ApiScope))
            .WithName("GetPassengerById")
            .WithApiVersionSet(builder.NewApiVersionSet("Passenger").Build())
            .Produces<GetPassengerByIdResponseDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Get Passenger By Id")
            .WithDescription("Get Passenger By Id")
            .WithOpenApi()
            .HasApiVersion(1.0);

        return builder;
    }
}

public class GetPassengerByIdValidator : AbstractValidator<GetPassengerById>
{
    public GetPassengerByIdValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Id is required!");
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

        if (passenger is null || (query.Requester is not null && !query.Requester.CanAccess(passenger.UserId)))
        {
            throw new PassengerNotFoundException();
        }

        var passengerDto = _mapper.Map<PassengerDto>(passenger);

        return new GetPassengerByIdResult(passengerDto);
    }
}