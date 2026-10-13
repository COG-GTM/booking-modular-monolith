namespace EndToEnd.Test.Routes;

public static class ApiRoutes
{
    private const string BaseApiPath = "api/v1.0";

    public static class Flight
    {
        public const string Id = "{id}";
        public const string GetFlightById = $"{BaseApiPath}/flight/{Id}";
        public const string CreateFlight = $"{BaseApiPath}/flight";
        public const string UpdateFlight = $"{BaseApiPath}/flight";
        public const string DeleteFlight = $"{BaseApiPath}/flight/{Id}";
        public const string CreateAircraft = $"{BaseApiPath}/flight/aircraft";
        public const string CreateAirport = $"{BaseApiPath}/flight/airport";
        public const string CreateSeat = $"{BaseApiPath}/flight/seat";
    }

    public static class Identity
    {
        public const string RegisterUser = $"{BaseApiPath}/identity/register-user";
    }
}
