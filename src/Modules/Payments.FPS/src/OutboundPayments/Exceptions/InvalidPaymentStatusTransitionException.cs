using BuildingBlocks.Exception;
using Payments.FPS.OutboundPayments.Enums;

namespace Payments.FPS.OutboundPayments.Exceptions;

public class InvalidPaymentStatusTransitionException(PaymentStatus from, PaymentStatus to)
    : SmartCharging.Infrastructure.Exceptions.DomainException(
        $"Invalid payment status transition from {from} to {to}."
    );
