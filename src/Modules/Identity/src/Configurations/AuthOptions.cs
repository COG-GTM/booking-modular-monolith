namespace Identity.Configurations;

public class AuthOptions
{
    public string IssuerUri { get; set; }

    public string ClientId { get; set; } = "client";

    public string ClientSecret { get; set; }

    public string SigningCertificatePath { get; set; }

    public string SigningCertificatePassword { get; set; }

    public bool SeedDefaultUsers { get; set; }

    public string SeedAdminPassword { get; set; }

    public string SeedUserPassword { get; set; }
}
