namespace Gateway.Extensions;

public sealed class CutoverOptions
{
    public string[] FrozenClusters { get; set; } = [];
    public int RetryAfterSeconds { get; set; } = 30;
}
