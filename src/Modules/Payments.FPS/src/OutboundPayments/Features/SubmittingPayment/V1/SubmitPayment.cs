namespace Payments.FPS.OutboundPayments.Features.SubmittingPayment.V1;

using System.Threading;
using Ardalis.GuardClauses;
using BuildingBlocks.Core.CQRS;
using BuildingBlocks.Core.Event;
using BuildingBlocks.Web;
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
using Payments.FPS.Data;
using Payments.FPS.OutboundPayments.Enums;
using Payments.FPS.OutboundPayments.Exceptions;
using Payments.FPS.OutboundPayments.Models;
using Payments.FPS.OutboundPayments.ValueObjects;

public record SubmitPayment(
    decimal Amount,
    string DebtorSortCode,
    string DebtorAccountNumber,
    string CreditorSortCode,
    string CreditorAccountNumber,
    string Reference
) : ICommand<SubmitPaymentResult>, IInternalCommand
{
    public Guid Id { get; init; } = NewId.NextGuid();
}

public record SubmitPaymentResult(Guid Id);

public record PaymentInitiatedDomainEvent(
    Guid Id,
    decimal Amount,
    string Currency,
    string DebtorSortCode,
    string DebtorAccountNumber,
    string CreditorSortCode,
    string CreditorAccountNumber,
    string Reference,
    PaymentStatus Status,
    bool IsDeleted
) : IDomainEvent;

public record PaymentSubmittedDomainEvent(
    Guid Id,
    decimal Amount,
    string Currency,
    string DebtorSortCode,
    string DebtorAccountNumber,
    string CreditorSortCode,
    string CreditorAccountNumber,
    string Reference,
    PaymentStatus Status,
    bool IsDeleted
) : IDomainEvent;

public record SubmitPaymentRequestDto(
    decimal Amount,
    string DebtorSortCode,
    string DebtorAccountNumber,
    string CreditorSortCode,
    string CreditorAccountNumber,
    string Reference
);

public record SubmitPaymentResponseDto(Guid Id);

public class SubmitPaymentEndpoint : IMinimalEndpoint
{
    public IEndpointRouteBuilder MapEndpoint(IEndpointRouteBuilder builder)
    {
        builder
            .MapPost(
                $"{EndpointConfig.BaseApiPath}/payments/fps",
                async (
                    SubmitPaymentRequestDto request,
                    IMediator mediator,
                    IMapper mapper,
                    CancellationToken cancellationToken
                ) =>
                {
                    var result = await mediator.Send(mapper.Map<SubmitPayment>(request), cancellationToken);
                    return Results.CreatedAtRoute(
                        "GetPaymentById",
                        new { id = result.Id },
                        result.Adapt<SubmitPaymentResponseDto>()
                    );
                }
            )
            .RequireAuthorization(nameof(ApiScope))
            .WithName("SubmitPayment")
            .WithApiVersionSet(builder.NewApiVersionSet("Payments").Build())
            .Produces<SubmitPaymentResponseDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithSummary("Submit Payment")
            .WithDescription("Submit Payment")
            .WithOpenApi()
            .HasApiVersion(1.0);
        return builder;
    }
}

public class SubmitPaymentValidator : AbstractValidator<SubmitPayment>
{
    public SubmitPaymentValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0).PrecisionScale(18, 2, true);
        RuleFor(x => x.DebtorSortCode).Matches("^\\d{6}$");
        RuleFor(x => x.DebtorAccountNumber).Matches("^\\d{8}$");
        RuleFor(x => x.CreditorSortCode).Matches("^\\d{6}$");
        RuleFor(x => x.CreditorAccountNumber).Matches("^\\d{8}$");
        RuleFor(x => x.Reference).NotEmpty().MaximumLength(35);
    }
}

internal class SubmitPaymentHandler(PaymentsDbContext db) : ICommandHandler<SubmitPayment, SubmitPaymentResult>
{
    public async Task<SubmitPaymentResult> Handle(SubmitPayment request, CancellationToken cancellationToken)
    {
        Guard.Against.Null(request, nameof(request));
        if (await db.OutboundPayments.AnyAsync(x => x.Id == request.Id, cancellationToken))
            throw new OutboundPaymentAlreadyExistException();
        var payment = OutboundPayment.Create(
            OutboundPaymentId.Of(request.Id),
            Amount.Of(request.Amount),
            UkAccount.Of(request.DebtorSortCode, request.DebtorAccountNumber),
            UkAccount.Of(request.CreditorSortCode, request.CreditorAccountNumber),
            request.Reference
        );
        payment.Submit();
        await db.OutboundPayments.AddAsync(payment, cancellationToken);
        return new SubmitPaymentResult(payment.Id);
    }
}
