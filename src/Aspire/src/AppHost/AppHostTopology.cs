using Microsoft.Extensions.Configuration;

internal static class AppHostTopology
{
    public const string ConfigurationKey = "AppHost:Topology";
    public const string Services = "Services";

    // Gateway clusters from src/Gateway appsettings; each has a single destination named "monolith".
    public static readonly string[] Clusters = ["identity", "flight", "passenger", "booking", "monolith"];

    public static string Resolve(IConfiguration configuration)
    {
        var value = configuration[ConfigurationKey];

        if (string.IsNullOrWhiteSpace(value) || value.Equals(Services, StringComparison.OrdinalIgnoreCase))
            return Services;

        throw new InvalidOperationException(
            $"Unknown {ConfigurationKey} '{value}'. Use '{Services}' (default); the 'Monolith' topology was "
                + "removed together with src/Api (AB-505)."
        );
    }
}
