using System.Diagnostics;

namespace BuildingBlocks.PersistMessageProcessor;

public static class PersistMessageActivitySource
{
    public const string Name = "BuildingBlocks.PersistMessageProcessor";

    public static readonly ActivitySource Instance = new(Name);
}
