namespace Gateway.Extensions;

public class GatewayCorsOptions
{
    public const string PolicyName = "gateway";

    public string[] AllowedOrigins { get; set; } = [];
}

public class GatewayRateLimitOptions
{
    public const string PolicyName = "gateway";

    public bool Enabled { get; set; } = true;
    public int PermitLimit { get; set; } = 100;
    public int WindowSeconds { get; set; } = 10;
    public int QueueLimit { get; set; }
}
