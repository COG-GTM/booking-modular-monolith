using System.Reflection;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Identity;
using Identity.Data;
using Identity.Extensions.Infrastructure;
using Identity.Host.Extensions;
using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Host.Test;

public class IdentityHostIsolationAndHealthTests : IdentityHostTestBase
{
    public IdentityHostIsolationAndHealthTests(TestWriteFixture<Identity.Host.Program, IdentityContext> fixture)
        : base(fixture) { }

    [Fact]
    public async Task standalone_host_exposes_health_endpoints()
    {
        using var client = Fixture.HttpClient;

        (await client.GetAsync("/alive")).StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var healthReport = await Fixture
            .ServiceProvider.GetRequiredService<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService>()
            .CheckHealthAsync();
        var unhealthyChecks = string.Join(
            "; ",
            healthReport
                .Entries.Where(entry =>
                    entry.Value.Status != Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy
                )
                .Select(entry => $"{entry.Key}: {entry.Value.Status} ({entry.Value.Description})")
        );
        healthReport
            .Status.Should()
            .Be(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy, unhealthyChecks);

        (await client.GetAsync("/health")).StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public void host_references_only_identity_and_registers_only_identity_outbox_and_consumers()
    {
        var referencedAssemblies = GetReferencedAssemblyNames(typeof(Identity.Host.Program).Assembly);
        referencedAssemblies.Should().NotContain(name => IsOtherModuleOrApi(name));

        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                ApplicationName = typeof(Identity.Host.Program).Assembly.GetName().Name,
                EnvironmentName = "test",
            }
        );
        foreach (
            var (key, value) in new Dictionary<string, string?>
            {
                ["HealthOptions:Enabled"] = "false",
                ["ObservabilityOptions:InstrumentationName"] = "identity_service",
                ["AuthOptions:IssuerUri"] = "http://localhost:5103",
                ["Jwt:Authority"] = "http://localhost:5103",
                ["Jwt:Audience"] = "booking-modular-monolith",
                ["MessagingOptions:Transport"] = "InMemory",
                ["PostgresOptions:ConnectionString:Identity"] =
                    "Server=localhost;Port=5432;Database=identity_service;User Id=postgres;Password=postgres",
                ["PersistMessageOptions:Enabled"] = "false",
            }
        )
        {
            builder.Configuration[key] = value;
        }

        builder.AddIdentityHostInfrastructure();
        builder.AddIdentityModules();

        var outboxModuleNames = builder
            .Services.Where(descriptor =>
                descriptor.ServiceType.IsGenericType
                && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(PersistMessageDbContext<>)
            )
            .Select(descriptor => descriptor.ServiceType.GenericTypeArguments[0].Name)
            .Distinct()
            .ToArray();
        outboxModuleNames.Should().Equal(nameof(IdentityRoot));

        var registeredConsumers = builder
            .Services.Where(descriptor =>
                (descriptor.ImplementationType ?? descriptor.ServiceType)
                    .GetInterfaces()
                    .Any(interfaceType =>
                        interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == typeof(IConsumer<>)
                    )
            )
            .Select(descriptor => descriptor.ImplementationType ?? descriptor.ServiceType)
            .Distinct()
            .ToArray();
        registeredConsumers.Should().BeEmpty();
    }

    private static HashSet<string> GetReferencedAssemblyNames(Assembly root)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var assembliesToVisit = new Queue<Assembly>([root]);

        while (assembliesToVisit.TryDequeue(out var assembly))
        {
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (!names.Add(reference.Name!))
                    continue;

                try
                {
                    assembliesToVisit.Enqueue(Assembly.Load(reference));
                }
                catch (FileNotFoundException) { }
            }
        }

        return names;
    }

    private static bool IsOtherModuleOrApi(string assemblyName) =>
        assemblyName is "Api" or "Booking" or "Flight" or "Passenger";
}
