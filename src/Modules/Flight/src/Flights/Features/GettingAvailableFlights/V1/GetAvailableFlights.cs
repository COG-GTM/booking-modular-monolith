using MongoDB.Driver.Linq;

namespace Flight.Flights.Features.GettingAvailableFlights.V1;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using BuildingBlocks.Caching;
using BuildingBlocks.Core.CQRS;
using BuildingBlocks.Web;
using Data;
using Dtos;
using Duende.IdentityServer.EntityFramework.Entities;
using Exceptions;
using Models;
using FluentValidation;
using Mapster;
using MapsterMapper;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MongoDB.Driver;

public record GetAvailableFlights(int PageNumber = 1, int PageSize = GetAvailableFlights.DefaultPageSize)
    : IQuery<GetAvailableFlightsResult>, ICacheRequest
{
    public const string CacheKeyPrefix = "GetAvailableFlights";
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
    public const int MaxPageNumber = 10_000;

    public string CacheKey => $"{CacheKeyPrefix}:{PageNumber}:{PageSize}";
    public DateTime? AbsoluteExpirationRelativeToNow => DateTime.Now.AddHours(1);
}

public record GetAvailableFlightsResult(IEnumerable<FlightDto> FlightDtos);

public record GetAvailableFlightsResponseDto(IEnumerable<FlightDto> FlightDtos);

public class GetAvailableFlightsEndpoint : IMinimalEndpoint
{
    public IEndpointRouteBuilder MapEndpoint(IEndpointRouteBuilder builder)
    {
        builder.MapGet($"{EndpointConfig.BaseApiPath}/flight/get-available-flights",
                async (int? pageNumber, int? pageSize, IMediator mediator, CancellationToken cancellationToken) =>
                {
                    var query = new GetAvailableFlights(pageNumber ?? 1,
                        pageSize ?? GetAvailableFlights.DefaultPageSize);

                    var result = await mediator.Send(query, cancellationToken);

                    var response = result.Adapt<GetAvailableFlightsResponseDto>();

                    return Results.Ok(response);
                })
            .RequireAuthorization(nameof(ApiScope))
            .WithName("GetAvailableFlights")
            .WithApiVersionSet(builder.NewApiVersionSet("Flight").Build())
            .Produces<GetAvailableFlightsResponseDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithSummary("Get Available Flights")
            .WithDescription("Get Available Flights")
            .WithOpenApi()
            .HasApiVersion(1.0);

        return builder;
    }
}

public class GetAvailableFlightsValidator : AbstractValidator<GetAvailableFlights>
{
    public GetAvailableFlightsValidator()
    {
        RuleFor(x => x.PageNumber).InclusiveBetween(1, GetAvailableFlights.MaxPageNumber)
            .WithMessage($"PageNumber must be between 1 and {GetAvailableFlights.MaxPageNumber}");
        RuleFor(x => x.PageSize).InclusiveBetween(1, GetAvailableFlights.MaxPageSize)
            .WithMessage($"PageSize must be between 1 and {GetAvailableFlights.MaxPageSize}");
    }
}

internal class GetAvailableFlightsHandler : IQueryHandler<GetAvailableFlights, GetAvailableFlightsResult>
{
    private readonly IMapper _mapper;
    private readonly FlightReadDbContext _flightReadDbContext;

    public GetAvailableFlightsHandler(IMapper mapper, FlightReadDbContext flightReadDbContext)
    {
        _mapper = mapper;
        _flightReadDbContext = flightReadDbContext;
    }

    public async Task<GetAvailableFlightsResult> Handle(GetAvailableFlights request,
        CancellationToken cancellationToken)
    {
        Guard.Against.Null(request, nameof(request));

        var flight = await BuildQuery(_flightReadDbContext.Flight.AsQueryable(), request)
            .ToListAsync(cancellationToken);

        if (flight.Count == 0)
        {
            throw new FlightNotFountException();
        }

        var flightDtos = _mapper.Map<IEnumerable<FlightDto>>(flight);

        return new GetAvailableFlightsResult(flightDtos);
    }

    internal static IQueryable<FlightReadModel> BuildQuery(IQueryable<FlightReadModel> flights,
        GetAvailableFlights request)
    {
        return flights
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.FlightDate)
            .ThenBy(x => x.Id)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize);
    }
}
