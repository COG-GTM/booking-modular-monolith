using Contracts.Grpc.Passenger.V1;
using Grpc.Core;
using MediatR;

namespace Passenger.GrpcServer.Services;

using Mapster;
using Passengers.Features.GettingPassengerById.V1;
using GetPassengerByIdResult = Contracts.Grpc.Passenger.V1.GetPassengerByIdResult;

public class PassengerGrpcServices : PassengerGrpcService.PassengerGrpcServiceBase
{
    private readonly IMediator _mediator;

    public PassengerGrpcServices(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override async Task<GetPassengerByIdResult> GetById(GetByIdRequest request, ServerCallContext context)
    {
        var result = await _mediator.Send(new GetPassengerById(new Guid(request.Id)));
        return result?.Adapt<GetPassengerByIdResult>();
    }
}