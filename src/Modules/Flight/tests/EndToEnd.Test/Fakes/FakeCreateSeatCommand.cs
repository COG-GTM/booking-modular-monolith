using AutoBogus;
using Flight.Seats.Enums;
using Flight.Seats.Features.CreatingSeat.V1;
using MassTransit;

namespace EndToEnd.Test.Fakes;

public sealed class FakeCreateSeatCommand : AutoFaker<CreateSeat>
{
    public FakeCreateSeatCommand(Guid flightId)
    {
        RuleFor(r => r.Id, _ => NewId.NextGuid());
        RuleFor(r => r.FlightId, _ => flightId);
        RuleFor(r => r.Class, _ => SeatClass.Economy);
        RuleFor(r => r.Type, _ => SeatType.Middle);
    }
}
