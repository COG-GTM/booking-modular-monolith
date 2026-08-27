using Grpc.Core;
using MediatR;
using Contracts.Grpc.Passenger;

namespace Passenger.GrpcServer.Services;

using Mapster;
using Passengers.Features.GettingPassengerById.V1;
using GetByIdRequest = Contracts.Grpc.Passenger.GetByIdRequest;
using GetPassengerByIdResult = Contracts.Grpc.Passenger.GetPassengerByIdResult;

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