using Microsoft.Extensions.Configuration;

namespace BuildingBlocks.Web;

public static class ModuleConfigurationExtensions
{
    public const string ModulesSectionName = "Modules";

    // Modules:<Module>:BackgroundProcessingEnabled (default true). Turned off in the monolith for a
    // module whose standalone service host is running, so the two never process the same work.
    public static bool IsModuleBackgroundProcessingEnabled(this IConfiguration configuration, string moduleName) =>
        configuration.GetValue($"{ModulesSectionName}:{moduleName}:BackgroundProcessingEnabled", true);
}
