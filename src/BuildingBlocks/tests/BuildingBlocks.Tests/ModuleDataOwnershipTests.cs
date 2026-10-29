using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.EFCore;
using BuildingBlocks.Mongo;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace BuildingBlocks.Tests;

public class ModuleDataOwnershipTests
{
    [Fact]
    public async Task modules_resolve_distinct_contexts_and_dispatch_to_their_own_processor()
    {
        var builder = CreateBuilder();
        builder.Configuration["PostgresOptions:ConnectionString:First"] = Connection("first");
        builder.Configuration["PostgresOptions:ConnectionString:Second"] = Connection("second");
        builder.AddPersistMessageProcessor<First>("First");
        builder.AddPersistMessageProcessor<Second>("Second");
        builder.Services.AddEventDispatcher();
        var first = Substitute.For<IPersistMessageProcessor<First>>();
        var second = Substitute.For<IPersistMessageProcessor<Second>>();
        builder.Services.AddScoped(_ => first);
        builder.Services.AddScoped(_ => second);
        await using var provider = builder.Services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var firstContext = services.GetRequiredService<PersistMessageDbContext<First>>();
        var secondContext = services.GetRequiredService<PersistMessageDbContext<Second>>();
        firstContext.Database.GetConnectionString().Should().Be(Connection("first"));
        secondContext.Database.GetConnectionString().Should().Be(Connection("second"));
        firstContext.Should().NotBeSameAs(secondContext);
        var envelope = new Published(Guid.NewGuid());
        await services.GetRequiredService<IEventDispatcher<First>>().SendAsync(envelope);
        await first
            .Received(1)
            .PublishMessageAsync(Arg.Is<MessageEnvelope>(x => x.Message == envelope), Arg.Any<CancellationToken>());
        second.ReceivedCalls().Should().BeEmpty();
        services
            .GetRequiredService<IEventDispatcher<Second>>()
            .Should()
            .NotBeSameAs(services.GetRequiredService<IEventDispatcher<First>>());
        services.GetService<IEventDispatcher>().Should().BeNull();
        services.GetService<IIntegrationEventPublisher>().Should().BeNull();
        services.GetService<IPersistMessageDbContext>().Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void connection_fallback_is_module_owned_and_evaluated_lazily(int firstAvailable)
    {
        var builder = CreateBuilder();
        builder.AddPersistMessageProcessor<First>("First");
        var keys = new[]
        {
            "ConnectionStrings:first-persist-message",
            "PersistMessageOptions:ConnectionString:First",
            "ConnectionStrings:first",
            "PostgresOptions:ConnectionString:First",
        };
        for (var i = firstAvailable; i < keys.Length; i++)
            builder.Configuration[keys[i]] = Connection("db" + i);
        using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope
            .ServiceProvider.GetRequiredService<PersistMessageDbContext<First>>()
            .Database.GetConnectionString()
            .Should()
            .Be(Connection("db" + firstAvailable));
    }

    [Fact]
    public void missing_connection_reports_the_module()
    {
        var builder = CreateBuilder();
        builder.AddPersistMessageProcessor<First>("First");
        using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var act = () => scope.ServiceProvider.GetRequiredService<PersistMessageDbContext<First>>();
        act.Should().Throw<InvalidOperationException>().WithMessage("*module 'First'*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void module_switch_disables_only_its_worker_and_global_switch_disables_all(bool globallyDisabled)
    {
        var builder = CreateBuilder();
        builder.Configuration["Modules:First:BackgroundProcessingEnabled"] = "false";
        if (globallyDisabled)
            builder.Configuration["PersistMessageOptions:Enabled"] = "false";
        builder.AddPersistMessageProcessor<First>("First");
        builder.AddPersistMessageProcessor<Second>("Second");
        var workers = builder
            .Services.Where(x => x.ServiceType == typeof(IHostedService))
            .Select(x => x.ImplementationType);
        workers.Should().NotContain(typeof(PersistMessageBackgroundService<First>));
        if (globallyDisabled)
            workers.Should().NotContain(typeof(PersistMessageBackgroundService<Second>));
        else
            workers.Should().Contain(typeof(PersistMessageBackgroundService<Second>));
        builder.Services.Should().Contain(x => x.ServiceType == typeof(IPersistMessageProcessor<First>));
    }

    [Fact]
    public void mongo_options_and_contexts_are_named_and_do_not_register_ambiguous_services()
    {
        var builder = CreateBuilder();
        builder.Configuration["MongoOptions:First:DatabaseName"] = "first_read";
        builder.Configuration["MongoOptions:Second:DatabaseName"] = "second_read";
        builder.Configuration["MongoOptions:ConnectionString"] = "mongodb://root:27017";
        builder.Configuration["ConnectionStrings:mongo"] = "mongodb://aspire:27017";
        builder.Configuration["MongoOptions:First:ConnectionString"] = "mongodb://first:27017";
        builder.AddMongoDbContext<FirstRead>("First");
        builder.AddMongoDbContext<SecondRead>("Second");
        using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<MongoOptions>>();
        options.Get("First").ConnectionString.Should().Be("mongodb://first:27017");
        options.Get("Second").ConnectionString.Should().Be("mongodb://aspire:27017");
        scope
            .ServiceProvider.GetRequiredService<FirstRead>()
            .Database.DatabaseNamespace.DatabaseName.Should()
            .Be("first_read");
        scope
            .ServiceProvider.GetRequiredService<SecondRead>()
            .Database.DatabaseNamespace.DatabaseName.Should()
            .Be("second_read");
        scope.ServiceProvider.GetService<IMongoDbContext>().Should().BeNull();
        scope.ServiceProvider.GetService<IMongoRepository<Document, Guid>>().Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void mongo_requires_a_module_database_name(string? database)
    {
        var builder = CreateBuilder();
        builder.Configuration["MongoOptions:ConnectionString"] = "mongodb://localhost";
        builder.Configuration["MongoOptions:First:DatabaseName"] = database;
        builder.AddMongoDbContext<FirstRead>("First");
        using var provider = builder.Services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IOptionsMonitor<MongoOptions>>().Get("First");
        act.Should().Throw<OptionsValidationException>().WithMessage("*MongoOptions:First:DatabaseName*");
    }

    [Fact]
    public async Task seeding_only_executes_seeders_from_the_requested_assembly()
    {
        var services = new ServiceCollection();
        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns("Development");
        var local = new LocalSeeder();
        var foreign = Substitute.For<IDataSeeder>();
        services.AddSingleton<IDataSeeder>(local);
        services.AddSingleton(foreign);
        using var provider = services.BuildServiceProvider();
        var manager = new SeedManager(NullLogger<SeedManager>.Instance, env, provider);
        await manager.ExecuteSeedAsync(typeof(LocalSeeder).Assembly);
        local.Calls.Should().Be(1);
        await foreign.DidNotReceive().SeedAllAsync();
    }

    private static WebApplicationBuilder CreateBuilder() =>
        WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "test", ContentRootPath = AppContext.BaseDirectory }
        );

    private static string Connection(string database) =>
        $"Host=localhost;Database={database};Username=test;Password=test";

    public sealed class First;

    public sealed class Second;

    private sealed record Published(Guid Id) : IIntegrationEvent;

    private sealed record Document : BuildingBlocks.Core.Model.Aggregate<Guid>;

    public sealed class FirstRead(IOptionsSnapshot<MongoOptions> options)
        : MongoDbContext(Options.Create(options.Get("First")));

    public sealed class SecondRead(IOptionsSnapshot<MongoOptions> options)
        : MongoDbContext(Options.Create(options.Get("Second")));

    private sealed class LocalSeeder : IDataSeeder
    {
        public int Calls { get; private set; }

        public Task SeedAllAsync()
        {
            Calls++;
            return Task.CompletedTask;
        }
    }
}
