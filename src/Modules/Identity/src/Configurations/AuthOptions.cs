namespace Identity.Configurations;

public class AuthOptions
{
    public string IssuerUri { get; set; }

    // Path to a PKCS#12 (.pfx/.p12) file holding the token signing certificate.
    // Loaded from a secret store / environment-injected location, never committed.
    public string SigningCertificatePath { get; set; }

    // Base64-encoded PKCS#12 signing certificate. Useful when the certificate is
    // provided directly through a secret (e.g. Key Vault, environment variable).
    public string SigningCertificateBase64 { get; set; }

    public string SigningCertificatePassword { get; set; }
}
