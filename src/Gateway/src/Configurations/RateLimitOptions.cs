namespace Gateway.Configurations;

public class RateLimitOptions
{
    public int PermitLimit { get; set; } = 100;
    public int WindowSeconds { get; set; } = 1;
    public int QueueLimit { get; set; }
}
