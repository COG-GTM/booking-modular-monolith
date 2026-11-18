namespace Identity.Configurations;

public class AuthOptions
{
    public string IssuerUri { get; set; }
    public string SigningKey { get; set; }
    public string SigningKeyPath { get; set; }
}
