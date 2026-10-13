namespace Identity.Configurations;

public class AuthOptions
{
    public string IssuerUri { get; set; }

    public string ClientId { get; set; } = "client";

    // Must be supplied from a managed secret (environment variable, user-secrets, vault).
    // When empty, no token client is registered and /connect/token rejects every request.
    public string? ClientSecret { get; set; }

    public bool HasClientCredentials => !string.IsNullOrWhiteSpace(ClientSecret);
}
