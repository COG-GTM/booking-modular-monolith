using BuildingBlocks.Exception;

namespace Payments.FPS.OutboundPayments.Exceptions;

public class InvalidUkAccountException()
    : SmartCharging.Infrastructure.Exceptions.DomainException(
        "Sort code must be 6 digits and account number 8 digits."
    );
