namespace Identity.Configurations;

public class AuthOptions
{
    public string IssuerUri { get; set; }

    // PEM-encoded RSA private key used to sign tokens; required outside Development, test and docker.
    public string SigningKeyPath { get; set; }
}