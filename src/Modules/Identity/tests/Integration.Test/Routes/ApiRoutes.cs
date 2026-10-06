namespace Integration.Test.Routes;

public static class ApiRoutes
{
    private const string BaseApiPath = "api/v1.0";

    public static class Identity
    {
        public const string RegisterUser = $"{BaseApiPath}/identity/register-user";
    }
}
