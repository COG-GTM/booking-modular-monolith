using BuildingBlocks.Exception;

namespace Payments.FPS.OutboundPayments.Exceptions;

public class OutboundPaymentNotFoundException() : NotFoundException("Outbound payment not found.");
