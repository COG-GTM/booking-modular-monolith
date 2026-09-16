namespace Payments.FPS.OutboundPayments.Features.GettingPaymentById.V1;

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
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Payments.FPS.Data;
using Payments.FPS.OutboundPayments.Dtos;
using Payments.FPS.OutboundPayments.Exceptions;

public record GetPaymentById(Guid Id) : IQuery<GetPaymentByIdResult>;

public record GetPaymentByIdResult(OutboundPaymentDto OutboundPaymentDto);

public record GetPaymentByIdResponseDto(OutboundPaymentDto OutboundPaymentDto);

public class GetPaymentByIdEndpoint : IMinimalEndpoint
{
    public IEndpointRouteBuilder MapEndpoint(IEndpointRouteBuilder builder)
    {
        builder
            .MapGet(
                $"{EndpointConfig.BaseApiPath}/payments/fps/{{id}}",
                async (Guid id, IMediator mediator, IMapper mapper, CancellationToken cancellationToken) =>
                    Results.Ok(
                        (
                            await mediator.Send(new GetPaymentById(id), cancellationToken)
                        ).Adapt<GetPaymentByIdResponseDto>()
                    )
            )
            .RequireAuthorization(nameof(ApiScope))
            .WithName("GetPaymentById")
            .WithApiVersionSet(builder.NewApiVersionSet("Payments").Build())
            .Produces<GetPaymentByIdResponseDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithSummary("Get Payment By Id")
            .WithDescription("Get Payment By Id")
            .WithOpenApi()
            .HasApiVersion(1.0);
        return builder;
    }
}

public class GetPaymentByIdValidator : AbstractValidator<GetPaymentById>
{
    public GetPaymentByIdValidator() => RuleFor(x => x.Id).NotEmpty();
}

internal class GetPaymentByIdHandler(PaymentsReadDbContext db, IMapper mapper)
    : IQueryHandler<GetPaymentById, GetPaymentByIdResult>
{
    public async Task<GetPaymentByIdResult> Handle(GetPaymentById request, CancellationToken cancellationToken)
    {
        Guard.Against.Null(request, nameof(request));
        var payment = await db
            .OutboundPayment.AsQueryable()
            .SingleOrDefaultAsync(x => x.OutboundPaymentId == request.Id && !x.IsDeleted, cancellationToken);
        if (payment is null)
            throw new OutboundPaymentNotFoundException();
        return new GetPaymentByIdResult(mapper.Map<OutboundPaymentDto>(payment));
    }
}
