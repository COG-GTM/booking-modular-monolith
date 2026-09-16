namespace Unit.Test.Fakes;

using BookingFlight;
using MassTransit;

public static class FakeReserveSeatResponse
{
    public static ReserveSeatResult Generate()
    {
        return new ReserveSeatResult { Id = NewId.NextGuid().ToString() };
    }
}
