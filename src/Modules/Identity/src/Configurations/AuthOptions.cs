namespace Identity.Configurations;

public class AuthOptions
{
    public string IssuerUri { get; set; }

    // Optional. When set, the signing key is loaded from/persisted to this file instead of the
    // host's own content root, so several Identity hosts (monolith + identity-service during the
    // cut-over window) issue tokens with the same key; automatic key management is turned off so the
    // file is the only signing key. Default: per-host tempkey.jwk in the content root.
    public string SigningKeyPath { get; set; }
}
