using BuildingBlocks.Exception;

namespace Payments.FPS.OutboundPayments.Exceptions;

public class OutboundPaymentAlreadyExistException()
    : BuildingBlocks.Exception.AppException("Outbound payment already exists.", System.Net.HttpStatusCode.Conflict);
