using System.Threading.Tasks;
using Contracts.Grpc.Flight;
using Grpc.Core;
using Mapster;
using MediatR;

namespace Flight.GrpcServer.Services;

using System;
using Flights.Features.GettingFlightById.V1;
using Seats.Features.GettingAvailableSeats.V1;
using Seats.Features.ReservingSeat.V1;
using GetAvailableSeatsRequest = Contracts.Grpc.Flight.GetAvailableSeatsRequest;
using GetAvailableSeatsResult = Contracts.Grpc.Flight.GetAvailableSeatsResult;
using GetByIdRequest = Contracts.Grpc.Flight.GetByIdRequest;
using GetFlightByIdResult = Contracts.Grpc.Flight.GetFlightByIdResult;
using ReserveSeatRequest = Contracts.Grpc.Flight.ReserveSeatRequest;
using ReserveSeatResult = Contracts.Grpc.Flight.ReserveSeatResult;
using SeatDtoResponse = Contracts.Grpc.Flight.SeatDtoResponse;

public class FlightGrpcServices : FlightGrpcService.FlightGrpcServiceBase
{
    private readonly IMediator _mediator;

    public FlightGrpcServices(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override async Task<GetFlightByIdResult> GetById(GetByIdRequest request, ServerCallContext context)
    {
        var result = await _mediator.Send(new GetFlightById(new Guid(request.Id)));
        return result.Adapt<GetFlightByIdResult>();
    }

    public override async Task<GetAvailableSeatsResult> GetAvailableSeats(GetAvailableSeatsRequest request, ServerCallContext context)
    {
        var result = new GetAvailableSeatsResult();

        var availableSeats = await _mediator.Send(new GetAvailableSeats(new Guid(request.FlightId)));

        if (availableSeats?.SeatDtos == null)
        {
            return result;
        }

        foreach (var availableSeat in availableSeats.SeatDtos)
        {
            result.SeatDtos.Add(availableSeat.Adapt<SeatDtoResponse>());
        }

        return result;
    }

    public override async Task<ReserveSeatResult> ReserveSeat(ReserveSeatRequest request, ServerCallContext context)
    {
        var result = await _mediator.Send(new ReserveSeat(new Guid(request.FlightId), request.SeatNumber));
        return result.Adapt<ReserveSeatResult>();
    }
}