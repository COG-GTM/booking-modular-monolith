namespace Identity.Configurations;

using System.ComponentModel.DataAnnotations;

public class TokenEndpointRateLimitOptions
{
    [Range(1, int.MaxValue)]
    public int PermitLimit { get; set; } = 10;

    [Range(1, int.MaxValue)]
    public int WindowSeconds { get; set; } = 60;
}
