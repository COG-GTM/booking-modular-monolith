using BuildingBlocks.Core.Event;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace BuildingBlocks.Unit.Test.Persistence;

public class PersistMessageExtensionsTests
{
    private static WebApplicationBuilder CreateBuilder(IEnumerable<KeyValuePair<string, string?>>? settings = null)
    {
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { EnvironmentName = "test" });
        if (settings is not null)
            builder.Configuration.AddInMemoryCollection(settings);
        return builder;
    }

    private static IConfiguration Config(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(t => new KeyValuePair<string, string?>(t.Key, t.Value)))
            .Build();

    [Fact]
    public void connection_string_prefers_aspire_connection_strings_key()
    {
        var configuration = Config(
            ("ConnectionStrings:flight-schedule-persist-message", "Host=aspire"),
            ("PersistMessageOptions:ConnectionString:FlightSchedule", "Host=options")
        );

        Extensions.GetPersistMessageConnectionString(configuration, "FlightSchedule").Should().Be("Host=aspire");
    }

    [Fact]
    public void connection_string_falls_back_to_persist_message_options()
    {
        var configuration = Config(("PersistMessageOptions:ConnectionString:FlightSchedule", "Host=options"));

        Extensions.GetPersistMessageConnectionString(configuration, "FlightSchedule").Should().Be("Host=options");
    }

    [Fact]
    public void connection_string_throws_argument_exception_naming_both_keys_when_missing()
    {
        var act = () => Extensions.GetPersistMessageConnectionString(Config(), "FlightSchedule");

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*PersistMessageOptions:ConnectionString:FlightSchedule*")
            .WithMessage("*ConnectionStrings:flight-schedule-persist-message*");
    }

    [Fact]
    public void add_persist_message_processor_registers_store_and_background_service_by_default()
    {
        var builder = CreateBuilder([new("PersistMessageOptions:ConnectionString:Flight", "Host=x;Database=y")]);

        builder.AddPersistMessageProcessor<PersistFlightModule>("Flight");

        builder
            .Services.Should()
            .ContainSingle(d => d.ServiceType == typeof(PersistMessageStore))
            .Which.ImplementationInstance.Should()
            .BeEquivalentTo(new PersistMessageStore("Flight", typeof(PersistMessageDbContext<PersistFlightModule>)));
        builder
            .Services.Should()
            .Contain(d =>
                d.ServiceType == typeof(IHostedService)
                && d.ImplementationType == typeof(PersistMessageBackgroundService<PersistFlightModule>)
            );
    }

    [Fact]
    public void add_persist_message_processor_skips_worker_when_disabled()
    {
        var builder = CreateBuilder(
            [
                new("PersistMessageOptions:ConnectionString:Flight", "Host=x;Database=y"),
                new("PersistMessageOptions:Enabled", "false"),
            ]
        );

        builder.AddPersistMessageProcessor<PersistFlightModule>("Flight");

        builder
            .Services.Should()
            .NotContain(d => d.ImplementationType == typeof(PersistMessageBackgroundService<PersistFlightModule>));
    }

    [Fact]
    public void add_persist_message_processor_skips_worker_when_module_background_disabled()
    {
        var builder = CreateBuilder(
            [
                new("PersistMessageOptions:ConnectionString:Flight", "Host=x;Database=y"),
                new("Modules:Flight:BackgroundProcessingEnabled", "false"),
            ]
        );

        builder.AddPersistMessageProcessor<PersistFlightModule>("Flight");

        builder
            .Services.Should()
            .NotContain(d => d.ImplementationType == typeof(PersistMessageBackgroundService<PersistFlightModule>));
    }

    [Fact]
    public void add_persist_message_processor_throws_for_empty_module_name()
    {
        var builder = CreateBuilder();

        var act = () => builder.AddPersistMessageProcessor<PersistFlightModule>("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task background_service_calls_process_all_until_stopped()
    {
        var moduleProcessor = Substitute.For<IPersistMessageProcessor<PersistFlightModule>>();
        var services = new ServiceCollection().AddSingleton(moduleProcessor).BuildServiceProvider();
        var options = Options.Create(new PersistMessageOptions { Interval = 0 });
        var service = new PersistMessageBackgroundService<PersistFlightModule>(
            Microsoft
                .Extensions
                .Logging
                .Abstractions
                .NullLogger<PersistMessageBackgroundService<PersistFlightModule>>
                .Instance,
            services,
            options
        );

        await service.StartAsync(CancellationToken.None);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (moduleProcessor.ReceivedCalls().Count() < 1 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }
        await service.StopAsync(CancellationToken.None);

        await moduleProcessor.Received().ProcessAllAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task integration_event_publisher_delegates_to_processor()
    {
        var processor = Substitute.For<IPersistMessageProcessor>();
        var publisher = new PersistMessageIntegrationEventPublisher(processor);
        var envelope = new MessageEnvelope(new PersistPing(Guid.NewGuid()));

        await publisher.PublishAsync(envelope);
        await publisher.AddInternalMessageAsync(new PersistCreateReadModel(Guid.NewGuid()));

        await processor.Received(1).PublishMessageAsync(envelope, Arg.Any<CancellationToken>());
        await processor
            .Received(1)
            .AddInternalMessageAsync(Arg.Any<PersistCreateReadModel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void persist_message_defaults_then_change_state_and_retry()
    {
        var id = Guid.NewGuid();
        var message = new PersistMessage(id, "type", "{}", MessageDeliveryType.Outbox);

        message.MessageStatus.Should().Be(MessageStatus.InProgress);
        message.RetryCount.Should().Be(0);
        message.Id.Should().Be(id);

        message.ChangeState(MessageStatus.Processed);
        message.IncreaseRetry();

        message.MessageStatus.Should().Be(MessageStatus.Processed);
        message.RetryCount.Should().Be(1);
    }
}
