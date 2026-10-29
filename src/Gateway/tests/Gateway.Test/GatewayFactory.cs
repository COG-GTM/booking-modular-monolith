using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Gateway.Test;

/// <summary>
/// Hosts the real gateway (shipped appsettings.json) in-memory. Settings are applied as configuration
/// overrides only, i.e. exactly what an operator would change via env vars.
/// </summary>
public sealed class GatewayFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "http://gateway.test";
    public const string Audience = "booking-modular-monolith";

    private static readonly SymmetricSecurityKey SigningKey = new(
        Encoding.UTF8.GetBytes("gateway-tests-signing-key-0123456789abcdef")
    );

    private readonly IDictionary<string, string?> _settings;

    public GatewayFactory(IDictionary<string, string?> settings)
    {
        _settings = settings;
    }

    public static GatewayFactory AllClustersTo(EchoBackend backend, IDictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>();
        foreach (var cluster in new[] { "identity", "flight", "passenger", "booking" })
            settings[ClusterAddressKey(cluster)] = backend.Address;

        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
            settings[key] = value;

        return new GatewayFactory(settings);
    }

    public const string CutoverConfigFileKey = "Cutover:ConfigFile";

    public static string ClusterAddressKey(string cluster) =>
        $"ReverseProxy:Clusters:{cluster}:Destinations:primary:Address";

    public static string CreateToken(string scope = Audience, string subject = "user-1")
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Subject = new ClaimsIdentity([new Claim("sub", subject), new Claim("scope", scope)]),
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("test");
        builder.UseSetting("Jwt:Authority", Issuer);
        // Read while the gateway builds its configuration, so it must be host-level rather than app configuration.
        if (_settings.TryGetValue(CutoverConfigFileKey, out var cutoverConfigFile))
            builder.UseSetting(CutoverConfigFileKey, cutoverConfigFile);
        builder.ConfigureAppConfiguration(
            (_, config) =>
                config.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ObservabilityOptions:UseOTLPExporter"] = "false",
                        ["ObservabilityOptions:UseAspireOTLPExporter"] = "false",
                    }.Concat(_settings)
                )
        );

        builder.ConfigureTestServices(services =>
            services.Configure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                options =>
                {
                    // Static signing key instead of fetching discovery/JWKS from IdentityServer.
                    options.Configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                    options.Configuration.SigningKeys.Add(SigningKey);
                    options.TokenValidationParameters.IssuerSigningKey = SigningKey;
                }
            )
        );
    }
}
