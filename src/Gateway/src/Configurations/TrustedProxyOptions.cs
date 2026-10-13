namespace Gateway.Configurations;

public class TrustedProxyOptions
{
    public string[] KnownProxies { get; set; } = [];
    public string[] KnownNetworks { get; set; } = [];
}
