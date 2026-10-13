namespace Gateway.Configurations;

// Off by default: the header discloses internal host names. Enable during cut-over/canary windows
// to see which destination (monolith or extracted service) served each response. Even when enabled,
// only callers that presented a valid bearer token get the header (AuthenticatedOnly = true), so it
// is never visible to anonymous internet traffic.
public class UpstreamHeaderOptions
{
    public const string HeaderName = "X-Gateway-Upstream";

    public bool Enabled { get; set; }

    public bool AuthenticatedOnly { get; set; } = true;
}
