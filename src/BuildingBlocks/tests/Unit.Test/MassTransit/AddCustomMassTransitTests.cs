using BuildingBlocks.MassTransit;
using FluentAssertions;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace Unit.Test.MassTransit;

public class AddCustomMassTransitTests
{
    [Fact]
    public void empty_assemblies_should_not_register_mass_transit_internal_consumers()
    {
        var (services, configuration) = CreateServices(out var environment);
        services.AddCustomMassTransit(configuration, environment, TransportType.InMemory);

        Action buildProvider = () =>
        {
            using var provider = services.BuildServiceProvider(
                new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
            );
        };

        buildProvider.Should().NotThrow();
        services
            .Should()
            .NotContain(descriptor =>
                descriptor.ServiceType.Equals(typeof(global::MassTransit.JobService.SuperviseJobConsumer))
            );
    }

    [Fact]
    public void consumer_assemblies_should_register_their_consumers()
    {
        var (services, configuration) = CreateServices(out var environment);
        services.AddCustomMassTransit(
            configuration,
            environment,
            TransportType.InMemory,
            typeof(TestMassTransitConsumer).Assembly
        );

        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(TestMassTransitConsumer));
    }

    private static (ServiceCollection Services, IConfiguration Configuration) CreateServices(
        out IWebHostEnvironment environment
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Development);
        environment.ApplicationName.Returns("Api");

        return (services, configuration);
    }
}

public sealed record TestMassTransitMessage;

public sealed class TestMassTransitConsumer : IConsumer<TestMassTransitMessage>
{
    public Task Consume(ConsumeContext<TestMassTransitMessage> context) => Task.CompletedTask;
}
