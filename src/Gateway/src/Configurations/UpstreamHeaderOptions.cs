namespace Gateway.Configurations;

// Off by default: the header discloses internal host names. Enable during cut-over/canary windows
// to see which destination (monolith or extracted service) served each response.
public class UpstreamHeaderOptions
{
    public const string HeaderName = "X-Gateway-Upstream";

    public bool Enabled { get; set; }
}
