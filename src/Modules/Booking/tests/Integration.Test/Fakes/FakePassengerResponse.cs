namespace Integration.Test.Fakes;

using System;
using BookingPassenger;
using MassTransit;

public static class FakePassengerResponse
{
    public static GetPassengerByIdResult Generate(Guid? ownerUserId = null)
    {
        var result = new GetPassengerByIdResult
        {
            PassengerDto = new PassengerResponse()
            {
                Id = NewId.NextGuid().ToString(),
                Name = "Test",
                PassportNumber = "121618",
                UserId = (ownerUserId ?? FakeCurrentUserProvider.UserId).ToString()
            }
        };

        return result;
    }
}
