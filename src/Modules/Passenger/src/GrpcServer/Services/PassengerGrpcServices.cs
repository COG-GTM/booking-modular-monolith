using Grpc.Core;
using MediatR;

namespace Passenger.GrpcServer.Services;

using Duende.IdentityServer.EntityFramework.Entities;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Passengers.Features.GettingPassengerById.V1;
using GetPassengerByIdResult = Passenger.GetPassengerByIdResult;

[Authorize(nameof(ApiScope))]
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

        if (!PassengerAccessPolicy.CanRead(context.GetHttpContext().User, result.UserId))
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied,
                "The current user is not allowed to read this passenger."));
        }

        return result.Adapt<GetPassengerByIdResult>();
    }
}
