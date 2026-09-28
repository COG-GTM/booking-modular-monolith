using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Contract.Test.Hosting;

/// <summary>
/// Every service host sits behind the YARP gateway, which rewrites <c>Host</c> to the destination and carries the
/// public origin in <c>X-Forwarded-*</c>. <c>AddServiceHost</c>/<c>UseServiceHost</c> must apply those headers so
/// generated URLs (<c>CreatedAtRoute</c>, discovery documents) point at the gateway — but only when the caller is a
/// trusted proxy: loopback by default, everything on the private network when <c>ForwardedHeaders:TrustAnyProxy</c>
/// is set (compose), or the explicit <c>ForwardedHeaders:KnownProxies</c> list.
/// </summary>
public class ServiceHostForwardedHeadersTests
{
    private const string GatewayHost = "localhost:3001";
    private const string ClientIp = "203.0.113.10";
    private static readonly IPAddress ComposeGatewayIp = IPAddress.Parse("172.18.0.5");
    private static readonly IPAddress OtherContainerIp = IPAddress.Parse("172.18.0.6");

    private static readonly Dictionary<string, string?> BaseSettings = new()
    {
        ["AppOptions:Name"] = "Forwarded-Headers-Test",
        ["ObservabilityOptions:InstrumentationName"] = "contract_test",
        ["ObservabilityOptions:MetricsEnabled"] = "false",
        ["ObservabilityOptions:TracingEnabled"] = "false",
        ["ObservabilityOptions:LoggingEnabled"] = "false",
        ["ObservabilityOptions:UsePrometheusExporter"] = "false",
        ["HealthOptions:Enabled"] = "false",
        ["Jwt:Authority"] = "https://localhost:4000",
        ["Jwt:Audience"] = "flight-api",
        ["RabbitMqOptions:HostName"] = "localhost",
        ["RabbitMqOptions:Port"] = "5672",
        ["RabbitMqOptions:UserName"] = "booking",
        ["RabbitMqOptions:Password"] = "booking",
        ["RabbitMqOptions:ExchangeName"] = "flight",
        ["PersistMessageOptions:Interval"] = "30",
        ["PersistMessageOptions:Enabled"] = "false",
        ["PersistMessageOptions:ConnectionString"] = "Server=localhost;Port=5432;Database=contract_test",
    };

    private static WebApplication BuildHost(params (string Key, string? Value)[] forwardedHeaderSettings)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "test" });

        builder.Configuration.AddInMemoryCollection(BaseSettings);
        builder.Configuration.AddInMemoryCollection(
            forwardedHeaderSettings.ToDictionary(x => $"ForwardedHeaders:{x.Key}", x => x.Value)
        );

        builder.AddServiceHost(typeof(Flight.Api.Program).Assembly, "flight-persist-message");

        return builder.Build();
    }

    private static ForwardedHeadersOptions OptionsOf(WebApplication app) =>
        app.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

    private static void ShouldBeLoopbackNetworkOnly(IList<Microsoft.AspNetCore.HttpOverrides.IPNetwork> networks)
    {
        var network = networks.Should().ContainSingle().Which;
        network.Prefix.Should().Be(IPAddress.Parse("127.0.0.0"));
        network.PrefixLength.Should().Be(8);
    }

    /// <summary>
    /// Runs a request that arrived from <paramref name="remoteIp"/> carrying the gateway's forwarded headers through
    /// the real <c>UseServiceHost</c> pipeline (built without starting the host) and returns what the service sees.
    /// </summary>
    private static async Task<HttpContext> SendThroughServiceHost(WebApplication app, IPAddress remoteIp)
    {
        HttpContext? observed = null;

        app.UseServiceHost();
        app.Run(context =>
        {
            observed = context;
            return Task.CompletedTask;
        });

        var pipeline = ((IApplicationBuilder)app).Build();

        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Request.Method = HttpMethods.Get;
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("flight-api", 81);
        context.Request.Path = "/api/v1/flight";
        context.Connection.RemoteIpAddress = remoteIp;
        context.Request.Headers["X-Forwarded-Host"] = GatewayHost;
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        context.Request.Headers["X-Forwarded-For"] = ClientIp;

        await pipeline(context);

        observed.Should().NotBeNull("the request should reach the terminal middleware after UseServiceHost");
        return observed!;
    }

    private static void ShouldSeePublicOrigin(HttpContext context)
    {
        context.Request.Host.Should().Be(new HostString(GatewayHost));
        context.Request.Scheme.Should().Be("https");
        context.Connection.RemoteIpAddress.Should().Be(IPAddress.Parse(ClientIp));
    }

    private static void ShouldSeeInternalOrigin(HttpContext context, IPAddress remoteIp)
    {
        context.Request.Host.Should().Be(new HostString("flight-api", 81));
        context.Request.Scheme.Should().Be("http");
        context.Connection.RemoteIpAddress.Should().Be(remoteIp);
    }

    [Fact]
    public void host_scheme_and_client_ip_are_all_taken_from_the_gateway_headers()
    {
        var options = OptionsOf(BuildHost());

        options.ForwardedHeaders.Should().HaveFlag(ForwardedHeaders.XForwardedFor);
        options.ForwardedHeaders.Should().HaveFlag(ForwardedHeaders.XForwardedProto);
        options.ForwardedHeaders.Should().HaveFlag(ForwardedHeaders.XForwardedHost);
    }

    [Fact]
    public void by_default_only_loopback_proxies_are_trusted()
    {
        var options = OptionsOf(BuildHost());

        options.KnownProxies.Should().BeEquivalentTo([IPAddress.IPv6Loopback]);
        ShouldBeLoopbackNetworkOnly(options.KnownNetworks);
    }

    [Fact]
    public async Task forwarded_headers_from_a_loopback_gateway_are_applied()
    {
        var context = await SendThroughServiceHost(BuildHost(), IPAddress.Loopback);

        ShouldSeePublicOrigin(context);
    }

    [Fact]
    public async Task forwarded_headers_from_an_unknown_caller_are_ignored_by_default()
    {
        var context = await SendThroughServiceHost(BuildHost(), ComposeGatewayIp);

        ShouldSeeInternalOrigin(context, ComposeGatewayIp);
    }

    [Fact]
    public void trust_any_proxy_clears_the_known_proxy_and_network_lists()
    {
        var options = OptionsOf(BuildHost(("TrustAnyProxy", "true")));

        options.KnownProxies.Should().BeEmpty();
        options.KnownNetworks.Should().BeEmpty();
    }

    [Fact]
    public async Task trust_any_proxy_applies_forwarded_headers_from_any_caller()
    {
        var context = await SendThroughServiceHost(BuildHost(("TrustAnyProxy", "true")), ComposeGatewayIp);

        ShouldSeePublicOrigin(context);
    }

    [Fact]
    public void configured_known_proxies_are_added_alongside_loopback()
    {
        var options = OptionsOf(BuildHost(("KnownProxies:0", "172.18.0.5"), ("KnownProxies:1", "10.0.0.7")));

        options
            .KnownProxies.Should()
            .BeEquivalentTo([IPAddress.IPv6Loopback, ComposeGatewayIp, IPAddress.Parse("10.0.0.7")]);
        ShouldBeLoopbackNetworkOnly(options.KnownNetworks);
    }

    [Fact]
    public async Task configured_known_proxy_is_trusted()
    {
        var context = await SendThroughServiceHost(BuildHost(("KnownProxies:0", "172.18.0.5")), ComposeGatewayIp);

        ShouldSeePublicOrigin(context);
    }

    [Fact]
    public async Task callers_outside_the_configured_known_proxies_are_still_ignored()
    {
        var context = await SendThroughServiceHost(BuildHost(("KnownProxies:0", "172.18.0.5")), OtherContainerIp);

        ShouldSeeInternalOrigin(context, OtherContainerIp);
    }

    [Fact]
    public void trust_any_proxy_is_only_enabled_in_the_docker_settings_of_every_service()
    {
        foreach (var service in new[] { "Flight", "Passenger", "Booking", "Identity" })
        {
            var root = System.IO.Path.Combine(FindRepoRoot(), "src", "Services", $"{service}.Api", "src");

            Setting(root, "appsettings.json").Should().BeNull($"{service} must not trust any proxy by default");
            bool.Parse(Setting(root, "appsettings.docker.json")!)
                .Should()
                .BeTrue($"{service} runs behind the compose gateway");
        }
    }

    private static string? Setting(string root, string file) =>
        new ConfigurationBuilder().AddJsonFile(System.IO.Path.Combine(root, file), optional: false).Build()[
            "ForwardedHeaders:TrustAnyProxy"
        ];

    private static string FindRepoRoot()
    {
        var directory = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (
            directory is not null
            && !System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "booking-modular-monolith.sln"))
        )
            directory = directory.Parent;

        return directory?.FullName ?? throw new System.IO.DirectoryNotFoundException("Repository root not found.");
    }
}
