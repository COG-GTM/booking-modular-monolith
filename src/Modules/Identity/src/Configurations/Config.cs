using Duende.IdentityServer;
using Duende.IdentityServer.Models;
using Identity.Identity.Constants;
using IdentityModel;

namespace Identity.Configurations;

public static class Config
{
    public static IEnumerable<IdentityResource> IdentityResources =>
        new List<IdentityResource>
        {
            new IdentityResources.OpenId(),
            new IdentityResources.Profile(),
            new IdentityResources.Email()
        };


    public static IEnumerable<ApiScope> ApiScopes =>
        new List<ApiScope>
        {
            new(Constants.StandardScopes.FlightApi),
            new(Constants.StandardScopes.PassengerApi),
            new(Constants.StandardScopes.BookingApi),
            new(Constants.StandardScopes.IdentityApi),
            new(Constants.StandardScopes.BookingModularMonolith),
            new(JwtClaimTypes.Role, new List<string> {"role"})
        };


    public static IList<ApiResource> ApiResources =>
        new List<ApiResource>
        {
            new(Constants.StandardScopes.FlightApi)
            {
                Scopes = { Constants.StandardScopes.FlightApi }
            },
            new(Constants.StandardScopes.PassengerApi)
            {
                Scopes = { Constants.StandardScopes.PassengerApi }
            },
            new(Constants.StandardScopes.BookingApi)
            {
                Scopes = { Constants.StandardScopes.BookingApi }
            },
            new(Constants.StandardScopes.IdentityApi)
            {
                Scopes = { Constants.StandardScopes.IdentityApi }
            },
            new(Constants.StandardScopes.BookingModularMonolith)
            {
                Scopes = { Constants.StandardScopes.BookingModularMonolith }
            },
        };

    public static IEnumerable<Client> GetClients(AuthOptions authOptions)
    {
        if (string.IsNullOrWhiteSpace(authOptions?.ClientSecret))
        {
            throw new InvalidOperationException(
                $"{nameof(AuthOptions)}:{nameof(AuthOptions.ClientSecret)} is not configured. "
                + "Provide it through configuration (e.g. the AuthOptions__ClientSecret environment variable or user secrets); "
                + "IdentityServer clients are not registered without it.");
        }

        return new List<Client>
        {
            new()
            {
                ClientId = "client",
                AllowedGrantTypes = GrantTypes.ResourceOwnerPassword,
                ClientSecrets =
                {
                    new Secret(authOptions.ClientSecret.Sha256())
                },
                AllowedScopes =
                {
                    IdentityServerConstants.StandardScopes.OpenId,
                    IdentityServerConstants.StandardScopes.Profile,
                    JwtClaimTypes.Role, // Include roles scope
                    Constants.StandardScopes.BookingModularMonolith, // the only API scope the monolith's ApiScope policy requires
                },
                AccessTokenLifetime = 3600,  // authorize the client to access protected resources
                IdentityTokenLifetime = 3600, // authenticate the user,
                AlwaysIncludeUserClaimsInIdToken = true // Include claims in ID token
            }
        };
    }
}
