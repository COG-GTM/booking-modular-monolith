namespace EndToEnd.Test.Routes;

public static class ApiRoutes
{
    private const string BaseApiPath = "api/v1.0";

    public static class Booking
    {
        public const string CreateBooking = $"{BaseApiPath}/booking";
    }
}
