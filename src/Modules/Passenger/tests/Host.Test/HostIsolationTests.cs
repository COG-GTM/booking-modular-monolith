using System.Reflection;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Passenger.Identity.Consumers.RegisteringNewUser.V1;
using Xunit;

namespace Host.Test;

public class HostIsolationTests
{
    private static readonly string[] ForbiddenAssemblies = ["Api", "Flight", "Identity", "Booking"];

    [Fact]
    public void host_should_only_reference_passenger_module()
    {
        // Arrange — walk the transitive referenced-assembly closure of the host
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toVisit = new Queue<AssemblyName>(typeof(global::Passenger.Host.Program).Assembly.GetReferencedAssemblies());

        while (toVisit.Count > 0)
        {
            var assemblyName = toVisit.Dequeue();
            if (!visited.Add(assemblyName.Name!))
            {
                continue;
            }

            Assembly? loaded = null;
            try
            {
                loaded = Assembly.Load(assemblyName);
            }
            catch (Exception)
            {
                // not loaded/available locally — no module assembly can hide behind it
            }

            if (loaded is null)
            {
                continue;
            }

            foreach (var referenced in loaded.GetReferencedAssemblies())
            {
                toVisit.Enqueue(referenced);
            }
        }

        // Assert
        visited.Should().Contain("Passenger");
        visited.Should().NotContain(ForbiddenAssemblies);
    }

    [Fact]
    public void host_should_consume_user_created_on_its_own_queue()
    {
        // Arrange — the host's own appsettings.json (linked into test output)
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("passenger-host-appsettings.json")
            .Build();

        var serviceName = configuration["MessageBroker:ServiceName"];

        // Act
        var queueName = new KebabCaseEndpointNameFormatter(serviceName, includeNamespace: false)
            .Consumer<RegisterNewUserHandler>();

        // Assert
        serviceName.Should().Be("passenger");
        queueName.Should().Be("passenger-register-new-user-handler");
    }
}
