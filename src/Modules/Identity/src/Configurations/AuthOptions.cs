namespace Identity.Configurations;

public class AuthOptions
{
    public string IssuerUri { get; set; }
    public string? SigningCertificatePath { get; set; }
    public string? SigningCertificatePassword { get; set; }
}
