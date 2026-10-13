namespace Identity.Configurations;

public class AuthOptions
{
    public string IssuerUri { get; set; }
    public SigningCertificateOptions SigningCertificate { get; set; } = new();
}

public class SigningCertificateOptions
{
    public string Path { get; set; }
    public string Password { get; set; }
}
