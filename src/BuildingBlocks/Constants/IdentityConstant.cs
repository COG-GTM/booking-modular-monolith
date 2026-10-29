namespace BuildingBlocks.Constants;

public static class IdentityConstant
{
    public static class Policy
    {
        // Same value as nameof(Duende.IdentityServer.EntityFramework.Entities.ApiScope), without the Duende dependency.
        public const string ApiScope = "ApiScope";
    }

    public static class Role
    {
        public const string Admin = "admin";
        public const string User = "user";
    }
}