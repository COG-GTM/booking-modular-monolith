namespace Identity.Configurations;

public class AuthOptions
{
    public string IssuerUri { get; set; }

    // Optional. When set, the signing key is loaded from/persisted to this file instead of the
    // host's own content root, so several Identity hosts (monolith + identity-service during the
    // cut-over window) issue tokens with the same key. Automatic key-management keys are stored
    // next to it. Default: per-host tempkey.jwk in the content root.
    public string SigningKeyPath { get; set; }
}
