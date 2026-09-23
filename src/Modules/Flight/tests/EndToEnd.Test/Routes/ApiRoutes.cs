namespace EndToEnd.Test.Routes;

public static class ApiRoutes
{
    private const string BaseApiPath = "api/v1.0";

    public static class Flight
    {
        public const string Id = "{id}";
        public const string GetFlightById = $"{BaseApiPath}/flight/{Id}";
        public const string CreateFlight = $"{BaseApiPath}/flight";
    }

    public static class Aircraft
    {
        public const string CreateAircraft = $"{BaseApiPath}/flight/aircraft";
    }
}