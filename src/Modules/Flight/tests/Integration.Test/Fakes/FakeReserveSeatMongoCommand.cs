namespace Integration.Test.Fakes;

using AutoBogus;
using global::Flight.Seats.Features.CreatingSeat.V1;
using global::Flight.Seats.Features.ReservingSeat.V1;

public sealed class FakeReserveSeatMongoCommand : AutoFaker<ReserveSeatMongo>
{
    public FakeReserveSeatMongoCommand(CreateSeatMongo seat)
    {
        RuleFor(r => r.Id, _ => seat.Id);
        RuleFor(r => r.SeatNumber, _ => seat.SeatNumber);
        RuleFor(r => r.Type, _ => seat.Type);
        RuleFor(r => r.Class, _ => seat.Class);
        RuleFor(r => r.FlightId, _ => seat.FlightId);
        RuleFor(r => r.IsDeleted, _ => true);
    }
}
