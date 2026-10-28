using BuildingBlocks.MassTransit;
using BuildingBlocks.PersistMessageProcessor;
using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Messaging.Integration.Test;

public class ConsumerModuleRegistrationTests
{
    private const string EndpointName = "passenger-registration-probe";

    [Fact]
    public void Registers_one_module_mapping_per_concrete_consumer_in_distinct_supplied_assemblies()
    {
        var assembly = typeof(ConsumerModuleRegistrationTests).Assembly;
        var services = new ServiceCollection();
        services.AddCustomMassTransit(Environment("test"), Config(), "passenger", assembly, assembly);
        var registrations = Registrations(services);
        var concreteConsumers = assembly
            .GetTypes()
            .Where(type =>
                type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false }
                && typeof(IConsumer).IsAssignableFrom(type)
            )
            .ToArray();

        Assert.Equal(concreteConsumers.Length, registrations.Length);
        Assert.All(
            concreteConsumers,
            consumer =>
                Assert.Single(
                    registrations,
                    registration => registration.EndpointName == EndpointNameFor(consumer, "passenger")
                )
        );
        Assert.Contains(
            registrations,
            registration =>
                registration.EndpointName == EndpointName
                && registration.ConsumerAssembly == typeof(ConsumerModuleRegistrationTests).Assembly
        );
        Assert.DoesNotContain(
            registrations,
            registration => registration.EndpointName.Contains("abstract-probe", StringComparison.Ordinal)
        );
        Assert.DoesNotContain(
            registrations,
            registration => registration.EndpointName.Contains("generic-probe", StringComparison.Ordinal)
        );

        var noAssemblies = new ServiceCollection();
        noAssemblies.AddCustomMassTransit(Environment("test"), Config(), "passenger");
        Assert.Empty(Registrations(noAssemblies));
    }

    [Fact]
    public async Task In_memory_consume_uses_the_registered_endpoint_mapping_and_runs_inside_the_inbox()
    {
        var processor = Substitute.For<IPersistMessageProcessor<RegistrationProbeModule>>();
        processor
            .ExecuteInboxAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>())
            .Returns(call => ((Func<Task>)call[2])());

        var services = new ServiceCollection().AddLogging();
        services.AddCustomMassTransit(
            Environment("test"),
            Config(),
            "passenger",
            typeof(ConsumerModuleRegistrationTests).Assembly
        );
        services.AddSingleton<ProbeState>();
        services.AddSingleton(new ModulePersistenceRegistration("Registration", typeof(RegistrationProbeModule)));
        services.AddSingleton<IPersistMessageProcessor<RegistrationProbeModule>>(processor);

        await using var provider = services.BuildServiceProvider();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        var message = new RegistrationProbeEvent();
        var messageId = Guid.NewGuid();
        await harness.Bus.Publish(message, context => context.MessageId = messageId);

        Assert.True(await harness.Consumed.Any<RegistrationProbeEvent>());
        Assert.True(
            await harness.GetConsumerHarness<RegistrationProbeConsumer>().Consumed.Any<RegistrationProbeEvent>()
        );

        await processor
            .Received(1)
            .ExecuteInboxAsync(messageId, $"/{EndpointName}", Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>());
    }

    private static ConsumerModuleRegistration[] Registrations(IServiceCollection services) =>
        services
            .Where(descriptor => descriptor.ServiceType == typeof(ConsumerModuleRegistration))
            .Select(descriptor => (ConsumerModuleRegistration)descriptor.ImplementationInstance!)
            .ToArray();

    private static string EndpointNameFor(Type consumer, string serviceName) =>
        (string)
            typeof(IEndpointNameFormatter)
                .GetMethod(nameof(IEndpointNameFormatter.Consumer))!
                .MakeGenericMethod(consumer)
                .Invoke(new KebabCaseEndpointNameFormatter(serviceName, false), null)!;

    private static IWebHostEnvironment Environment(string name)
    {
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns(name);
        return environment;
    }

    private static IConfiguration Config() => new ConfigurationBuilder().Build();
}

public sealed class RegistrationProbeConsumer : IConsumer<RegistrationProbeEvent>
{
    public Task Consume(ConsumeContext<RegistrationProbeEvent> context) => Task.CompletedTask;
}

public abstract class AbstractProbeConsumer : IConsumer<RegistrationProbeEvent>
{
    public Task Consume(ConsumeContext<RegistrationProbeEvent> context) => Task.CompletedTask;
}

public sealed class GenericProbeConsumer<T> : IConsumer<T>
    where T : class
{
    public Task Consume(ConsumeContext<T> context) => Task.CompletedTask;
}

public sealed record RegistrationProbeEvent;

public sealed class RegistrationProbeModule { }
