using System;
using System.Collections.Generic;
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
        if (authOptions is null || !authOptions.HasClientCredentials)
        {
            return Array.Empty<Client>();
        }

        return new List<Client>
        {
            new()
            {
                ClientId = authOptions.ClientId,
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
                    Constants.StandardScopes.FlightApi,
                    Constants.StandardScopes.PassengerApi,
                    Constants.StandardScopes.BookingApi,
                    Constants.StandardScopes.IdentityApi,
                    Constants.StandardScopes.BookingModularMonolith,
                },
                AccessTokenLifetime = 3600,  // authorize the client to access protected resources
                IdentityTokenLifetime = 3600, // authenticate the user,
                AlwaysIncludeUserClaimsInIdToken = true // Include claims in ID token
            }
        };
    }
}
