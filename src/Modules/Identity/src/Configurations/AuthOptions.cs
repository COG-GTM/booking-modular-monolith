using System.ComponentModel.DataAnnotations;

namespace Identity.Configurations;

public class AuthOptions
{
    public string IssuerUri { get; set; }

    [Required]
    public string ClientSecret { get; set; }
}
