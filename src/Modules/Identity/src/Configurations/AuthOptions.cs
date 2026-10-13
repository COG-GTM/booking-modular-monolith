namespace Identity.Configurations;

using Duende.IdentityServer.Models;

public class AuthOptions
{
    public string IssuerUri { get; set; }

    public IList<ClientOptions> Clients { get; set; } = new List<ClientOptions>();
}

/// <summary>
/// An OAuth client registered with the Identity module. Bound from the <c>AuthOptions:Clients</c>
/// configuration section so that client secrets live in environment variables, user secrets or a
/// secret store instead of source control.
/// </summary>
public class ClientOptions
{
    public const int MinimumSecretLength = 32;

    public string ClientId { get; set; }

    /// <summary>
    /// Plain-text client secret supplied via configuration (never commit it); it is hashed before
    /// being handed to IdentityServer. Required for <c>client_credentials</c> clients, optional for
    /// <c>authorization_code</c> clients (a public client must use PKCE).
    /// </summary>
    public string ClientSecret { get; set; }

    /// <summary>
    /// Either <see cref="GrantType.ClientCredentials"/> (machine-to-machine) or
    /// <see cref="GrantType.AuthorizationCode"/> (interactive user login, PKCE enforced).
    /// The resource-owner-password grant is intentionally not supported.
    /// </summary>
    public string GrantType { get; set; } = Duende.IdentityServer.Models.GrantType.ClientCredentials;

    public IList<string> AllowedScopes { get; set; } = new List<string>();

    public IList<string> RedirectUris { get; set; } = new List<string>();

    public IList<string> PostLogoutRedirectUris { get; set; } = new List<string>();

    public IList<string> AllowedCorsOrigins { get; set; } = new List<string>();

    public int AccessTokenLifetime { get; set; } = 3600;

    public int IdentityTokenLifetime { get; set; } = 300;
}
