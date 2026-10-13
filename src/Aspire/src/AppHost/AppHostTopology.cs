using Microsoft.Extensions.Configuration;

internal static class AppHostTopology
{
    public const string ConfigurationKey = "AppHost:Topology";
    public const string Services = "Services";
    public const string Monolith = "Monolith";

    // Gateway clusters from src/Gateway appsettings; each has a single destination named "monolith".
    public static readonly string[] Clusters = ["identity", "flight", "passenger", "booking", "monolith"];

    public static string Resolve(IConfiguration configuration)
    {
        var value = configuration[ConfigurationKey];

        if (string.IsNullOrWhiteSpace(value) || value.Equals(Services, StringComparison.OrdinalIgnoreCase))
            return Services;

        if (value.Equals(Monolith, StringComparison.OrdinalIgnoreCase))
            return Monolith;

        throw new InvalidOperationException(
            $"Unknown {ConfigurationKey} '{value}'. Use '{Services}' (default) or '{Monolith}'."
        );
    }
}
