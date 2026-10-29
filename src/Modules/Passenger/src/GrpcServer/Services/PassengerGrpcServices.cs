using Grpc.Core;
using MediatR;
using Passenger.Passengers.Exceptions;

namespace Passenger.GrpcServer.Services;

using Mapster;
using Passengers.Features.GettingPassengerById.V1;
using GetPassengerByIdResult = Passenger.GetPassengerByIdResult;

public class PassengerGrpcServices : PassengerGrpcService.PassengerGrpcServiceBase
{
    private readonly IMediator _mediator;

    public PassengerGrpcServices(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override async Task<GetPassengerByIdResult> GetById(GetByIdRequest request, ServerCallContext context)
    {
        try
        {
            var result = await _mediator.Send(new GetPassengerById(new Guid(request.Id)), context.CancellationToken);
            return result?.Adapt<GetPassengerByIdResult>();
        }
        catch (PassengerNotFoundException)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Passenger not found!"));
        }
    }
}
