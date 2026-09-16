using BuildingBlocks.Exception;

namespace Payments.FPS.OutboundPayments.Exceptions;

public class InvalidAmountException()
    : SmartCharging.Infrastructure.Exceptions.DomainException(
        "Amount must be a positive GBP value with at most 2 decimal places."
    );
