namespace Identity.Configurations;

using System.ComponentModel.DataAnnotations;

public class AuthOptions
{
    public string IssuerUri { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string ClientSecret { get; set; }
}
