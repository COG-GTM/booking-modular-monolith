namespace EndToEnd.Test.Routes;

public static class ApiRoutes
{
    private const string BaseApiPath = "api/v1.0";

    public static class Passenger
    {
        public const string Id = "{id}";
        public const string GetPassengerById = $"{BaseApiPath}/passenger/{Id}";
        public const string CompleteRegistration = $"{BaseApiPath}/passenger/complete-registration";
    }
}
