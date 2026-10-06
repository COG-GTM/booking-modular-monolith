using AutoBogus;

namespace EndToEnd.Test.Fakes;

using global::Flight.Data.Seed;
using global::Flight.Seats.Enums;
using global::Flight.Seats.Features.CreatingSeat.V1;
using MassTransit;

public sealed class FakeCreateSeatCommand : AutoFaker<CreateSeat>
{
    public FakeCreateSeatCommand()
    {
        RuleFor(r => r.Id, _ => NewId.NextGuid());
        RuleFor(r => r.SeatNumber, _ => "14A");
        RuleFor(r => r.Type, _ => SeatType.Window);
        RuleFor(r => r.Class, _ => SeatClass.Economy);
        RuleFor(r => r.FlightId, _ => InitialData.Flights.First().Id.Value);
    }
}
