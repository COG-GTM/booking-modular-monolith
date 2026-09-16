using System;
using BookingFlight;
using MassTransit;

namespace Unit.Test.Fakes;

public static class FakeGetAvailableSeatsResponse
{
    public static GetAvailableSeatsResult Generate()
    {
        var result = new GetAvailableSeatsResult();
        result.SeatDtos.Add(
            new SeatDtoResponse
            {
                FlightId = new Guid("3c5c0000-97c6-fc34-2eb9-08db322230c9").ToString(),
                Class = SeatClass.Economy,
                Type = SeatType.Aisle,
                SeatNumber = "33F",
                Id = NewId.NextGuid().ToString(),
            }
        );

        return result;
    }
}
