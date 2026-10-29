using System.Reflection;
using BuildingBlocks.Core.Event;

namespace Contracts.Messages.Tests;

internal static class ContractTypes
{
    public static readonly Assembly Assembly = typeof(IIntegrationEvent).Assembly;

    public static IReadOnlyList<Type> Messages { get; } =
        Assembly.GetExportedTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IIntegrationEvent).IsAssignableFrom(t))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();

    public static string RepoRoot { get; } = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "booking-modular-monolith.sln")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
