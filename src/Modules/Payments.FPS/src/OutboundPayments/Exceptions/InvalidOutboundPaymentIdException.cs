using BuildingBlocks.Exception;

namespace Payments.FPS.OutboundPayments.Exceptions;

public class InvalidOutboundPaymentIdException(Guid value)
    : SmartCharging.Infrastructure.Exceptions.DomainException($"Outbound payment id {value} is invalid.");
