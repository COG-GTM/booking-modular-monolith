using System.Collections.Concurrent;
using BuildingBlocks.Exception;
using BuildingBlocks.MassTransit;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Unit.Test.Messaging;

public class RetryPolicyTests
{
    [Fact]
    public async Task transient_failure_should_be_retried_three_times_before_the_message_faults()
    {
        await using var provider = BuildProvider();
        var harness = await StartAsync(provider);

        await harness.Bus.Publish(new RetryPolicyMessage(ThrowValidationException: false));

        (await harness.Published.Any<Fault<RetryPolicyMessage>>()).Should().BeTrue();
        provider.GetRequiredService<RetryAttemptCounter>().Attempts.Should().Be(4, "1 attempt + 3 retries");
    }

    [Fact]
    public async Task validation_exception_should_fault_immediately_without_retries()
    {
        await using var provider = BuildProvider();
        var harness = await StartAsync(provider);

        await harness.Bus.Publish(new RetryPolicyMessage(ThrowValidationException: true));

        (await harness.Published.Any<Fault<RetryPolicyMessage>>()).Should().BeTrue();
        provider.GetRequiredService<RetryAttemptCounter>().Attempts.Should().Be(1);
    }

    private static ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    [$"{MessageBrokerOptions.SectionName}:TransportType"] = "InMemory",
                    [$"{MessageBrokerOptions.SectionName}:ServiceName"] = "retry-svc",
                    [$"{MessageBrokerOptions.SectionName}:UseInbox"] = "false",
                }
            )
            .Build();

        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns("test");

        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton<RetryAttemptCounter>()
            .AddSingleton(Substitute.For<IPersistMessageProcessor>());

        services.AddCustomMassTransit(configuration, environment, typeof(RetryPolicyHandler).Assembly);

        return services.BuildServiceProvider();
    }

    private static async Task<ITestHarness> StartAsync(IServiceProvider provider)
    {
        var harness = provider.GetRequiredService<ITestHarness>();
        harness.TestInactivityTimeout = TimeSpan.FromSeconds(10);
        harness.TestTimeout = TimeSpan.FromSeconds(30);
        await harness.Start();

        return harness;
    }

    public record RetryPolicyMessage(bool ThrowValidationException);

    public class RetryAttemptCounter
    {
        private int _attempts;

        public int Attempts => _attempts;

        public void Record() => Interlocked.Increment(ref _attempts);
    }

    public class RetryPolicyHandler : IConsumer<RetryPolicyMessage>
    {
        private readonly RetryAttemptCounter _counter;

        public RetryPolicyHandler(RetryAttemptCounter counter)
        {
            _counter = counter;
        }

        public Task Consume(ConsumeContext<RetryPolicyMessage> context)
        {
            _counter.Record();

            if (context.Message.ThrowValidationException)
            {
                throw new ValidationException("invalid message");
            }

            throw new InvalidOperationException("transient failure");
        }
    }
}
