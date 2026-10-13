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
            new IdentityResources.Email(),
        };

    public static IEnumerable<ApiScope> ApiScopes =>
        new List<ApiScope>
        {
            new(Constants.StandardScopes.FlightApi),
            new(Constants.StandardScopes.PassengerApi),
            new(Constants.StandardScopes.BookingApi),
            new(Constants.StandardScopes.IdentityApi),
            new(Constants.StandardScopes.BookingModularMonolith),
            new(JwtClaimTypes.Role, new List<string> { "role" }),
        };

    public static IList<ApiResource> ApiResources =>
        new List<ApiResource>
        {
            new(Constants.StandardScopes.FlightApi) { Scopes = { Constants.StandardScopes.FlightApi } },
            new(Constants.StandardScopes.PassengerApi) { Scopes = { Constants.StandardScopes.PassengerApi } },
            new(Constants.StandardScopes.BookingApi) { Scopes = { Constants.StandardScopes.BookingApi } },
            new(Constants.StandardScopes.IdentityApi) { Scopes = { Constants.StandardScopes.IdentityApi } },
            new(Constants.StandardScopes.BookingModularMonolith)
            {
                Scopes = { Constants.StandardScopes.BookingModularMonolith },
            },
        };

    /// <summary>
    /// Builds the IdentityServer clients from <see cref="AuthOptions.Clients"/>. No client is registered
    /// unless it is explicitly configured, and misconfigured clients fail fast at startup rather than
    /// silently exposing the token endpoint.
    /// </summary>
    public static IEnumerable<Client> GetClients(AuthOptions authOptions)
    {
        var clients = (authOptions?.Clients ?? new List<ClientOptions>()).Select(CreateClient).ToList();

        var duplicateClientId = clients
            .GroupBy(x => x.ClientId, StringComparer.Ordinal)
            .FirstOrDefault(x => x.Count() > 1)
            ?.Key;

        if (duplicateClientId is not null)
        {
            throw new InvalidOperationException(
                $"AuthOptions:Clients contains more than one client with ClientId '{duplicateClientId}'."
            );
        }

        return clients;
    }

    private static Client CreateClient(ClientOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            throw new InvalidOperationException("Every entry in AuthOptions:Clients must specify a ClientId.");
        }

        var client = new Client
        {
            ClientId = options.ClientId,
            AllowedScopes = ValidateScopes(options),
            RedirectUris = options.RedirectUris ?? new List<string>(),
            PostLogoutRedirectUris = options.PostLogoutRedirectUris ?? new List<string>(),
            AllowedCorsOrigins = options.AllowedCorsOrigins ?? new List<string>(),
            AccessTokenLifetime = options.AccessTokenLifetime,
            IdentityTokenLifetime = options.IdentityTokenLifetime,
            AlwaysIncludeUserClaimsInIdToken = false,
            AllowOfflineAccess = false,
        };

        switch (options.GrantType)
        {
            case GrantType.ClientCredentials:
                client.AllowedGrantTypes = GrantTypes.ClientCredentials;
                client.ClientSecrets.Add(CreateSecret(options));
                break;

            case GrantType.AuthorizationCode:
                client.AllowedGrantTypes = GrantTypes.Code;
                client.RequirePkce = true;
                client.AllowPlainTextPkce = false;

                if (client.RedirectUris.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Client '{options.ClientId}' uses the authorization_code grant and must configure at least one RedirectUri."
                    );
                }

                if (string.IsNullOrWhiteSpace(options.ClientSecret))
                {
                    client.RequireClientSecret = false;
                }
                else
                {
                    client.ClientSecrets.Add(CreateSecret(options));
                }

                break;

            default:
                throw new InvalidOperationException(
                    $"Client '{options.ClientId}' uses unsupported grant type '{options.GrantType}'. "
                        + $"Only '{GrantType.ClientCredentials}' and '{GrantType.AuthorizationCode}' are allowed."
                );
        }

        return client;
    }

    private static Secret CreateSecret(ClientOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            throw new InvalidOperationException(
                $"Client '{options.ClientId}' requires a ClientSecret. Provide it through configuration "
                    + $"(e.g. the AuthOptions__Clients__0__ClientSecret environment variable or a secret store), never in source control."
            );
        }

        if (options.ClientSecret.Length < ClientOptions.MinimumSecretLength)
        {
            throw new InvalidOperationException(
                $"Client '{options.ClientId}' has a ClientSecret shorter than {ClientOptions.MinimumSecretLength} characters."
            );
        }

        return new Secret(options.ClientSecret.Sha256());
    }

    private static ICollection<string> ValidateScopes(ClientOptions options)
    {
        var knownScopes = IdentityResources
            .Select(x => x.Name)
            .Concat(ApiScopes.Select(x => x.Name))
            .ToHashSet(StringComparer.Ordinal);

        var requestedScopes = (options.AllowedScopes ?? new List<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var unknownScope = requestedScopes.FirstOrDefault(x => !knownScopes.Contains(x));

        if (unknownScope is not null)
        {
            throw new InvalidOperationException(
                $"Client '{options.ClientId}' is configured with unknown scope '{unknownScope}'."
            );
        }

        return requestedScopes;
    }
}
