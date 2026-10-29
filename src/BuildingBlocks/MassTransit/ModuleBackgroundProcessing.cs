using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace BuildingBlocks.MassTransit;

public static class ModuleBackgroundProcessing
{
    public static bool IsEnabled(IConfiguration configuration, string moduleName) =>
        configuration.GetValue<bool?>($"Modules:{moduleName}:BackgroundProcessingEnabled") != false;

    public static Assembly[] ConsumerAssemblies(
        IConfiguration configuration,
        params (string Module, Assembly Assembly)[] modules
    ) => modules.Where(module => IsEnabled(configuration, module.Module)).Select(module => module.Assembly).ToArray();
}
