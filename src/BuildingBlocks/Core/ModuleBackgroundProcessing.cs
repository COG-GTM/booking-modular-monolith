using Microsoft.Extensions.Configuration;

namespace BuildingBlocks.Core;

public static class ModuleBackgroundProcessing
{
    public const string SectionName = "Modules";
    public const string EnabledKey = "BackgroundProcessingEnabled";

    public static bool IsModuleBackgroundProcessingEnabled(this IConfiguration configuration, string moduleName) =>
        configuration.GetValue($"{SectionName}:{moduleName}:{EnabledKey}", true);

    public static IReadOnlyList<T> WhereModuleBackgroundProcessingEnabled<T>(
        this IConfiguration configuration,
        IEnumerable<(string ModuleName, T Item)> modules
    ) =>
        modules
            .Where(module => configuration.IsModuleBackgroundProcessingEnabled(module.ModuleName))
            .Select(module => module.Item)
            .ToList();
}
