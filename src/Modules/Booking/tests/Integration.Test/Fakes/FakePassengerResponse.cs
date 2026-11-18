namespace Integration.Test.Fakes;

using BookingPassenger;
using MassTransit;

public static class FakePassengerResponse
{
    public static readonly Guid OwnerUserId = new("7d1c3f0a-5b2e-4c8f-9a41-2f6e8b9d0c11");

    public static GetPassengerByIdResult Generate()
    {
        var result = new GetPassengerByIdResult
        {
            PassengerDto = new PassengerResponse()
            {
                Id = NewId.NextGuid().ToString(),
                Name = "Test",
                PassportNumber = "121618",
                UserId = OwnerUserId.ToString()
            }
        };

        return result;
    }
}