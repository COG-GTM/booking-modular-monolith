namespace EndToEnd.Test.Routes;

public static class ApiRoutes
{
    private const string BaseApiPath = "api/v1.0";

    public static class Payments
    {
        public const string Id = "{id}";
        public const string SubmitPayment = $"{BaseApiPath}/payments/fps";
        public const string GetPaymentById = $"{BaseApiPath}/payments/fps/{Id}";
    }
}
