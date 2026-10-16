using BuildingBlocks.Mongo;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace BuildingBlocks.Unit.Test.Persistence;

public class MongoExtensionsTests
{
    private static WebApplicationBuilder CreateBuilder(params (string Key, string? Value)[] settings)
    {
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { EnvironmentName = "test" });
        builder.Configuration.AddInMemoryCollection(
            settings.Select(t => new KeyValuePair<string, string?>(t.Key, t.Value))
        );
        return builder;
    }

    [Fact]
    public async Task named_options_resolve_connection_string_by_precedence()
    {
        var builder = CreateBuilder(
            ("MongoOptions:Flight:ConnectionString", "mongodb://module"),
            ("ConnectionStrings:flight-mongo", "mongodb://aspire-module"),
            ("ConnectionStrings:mongo", "mongodb://aspire"),
            ("MongoOptions:ConnectionString", "mongodb://root"),
            ("MongoOptions:Flight:DatabaseName", "flight_db")
        );

        builder.AddMongoDbContext<PersistMongoDbContext>("Flight");

        await using var provider = builder.Services.BuildServiceProvider();
        provider
            .GetRequiredService<IOptionsMonitor<MongoOptions>>()
            .Get("Flight")
            .ConnectionString.Should()
            .Be("mongodb://module");
    }

    [Fact]
    public async Task named_options_fall_back_through_aspire_and_root_keys()
    {
        var builder = CreateBuilder(
            ("ConnectionStrings:mongo", "mongodb://aspire"),
            ("MongoOptions:ConnectionString", "mongodb://root"),
            ("MongoOptions:Flight:DatabaseName", "flight_db")
        );

        builder.AddMongoDbContext<PersistMongoDbContext>("Flight");

        await using var provider = builder.Services.BuildServiceProvider();
        provider
            .GetRequiredService<IOptionsMonitor<MongoOptions>>()
            .Get("Flight")
            .ConnectionString.Should()
            .Be("mongodb://aspire");
    }

    [Fact]
    public async Task missing_connection_string_fails_validation()
    {
        var builder = CreateBuilder(("MongoOptions:Flight:DatabaseName", "flight_db"));

        builder.AddMongoDbContext<PersistMongoDbContext>("Flight");

        await using var provider = builder.Services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IOptionsMonitor<MongoOptions>>().Get("Flight");

        act.Should()
            .Throw<OptionsValidationException>()
            .WithMessage("*Mongo connection string for module 'Flight' not found*");
    }

    [Fact]
    public async Task missing_database_name_fails_validation()
    {
        var builder = CreateBuilder(("MongoOptions:Flight:ConnectionString", "mongodb://module"));

        builder.AddMongoDbContext<PersistMongoDbContext>("Flight");

        await using var provider = builder.Services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IOptionsMonitor<MongoOptions>>().Get("Flight");

        act.Should().Throw<OptionsValidationException>().WithMessage("*MongoOptions:Flight:DatabaseName*required*");
    }

    [Fact]
    public async Task resolved_context_uses_configured_database_name()
    {
        var builder = CreateBuilder(
            ("MongoOptions:Flight:ConnectionString", "mongodb://module"),
            ("MongoOptions:Flight:DatabaseName", "flight_db")
        );

        builder.AddMongoDbContext<PersistMongoDbContext>("Flight");

        await using var scope = builder.Services.BuildServiceProvider().CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<PersistMongoDbContext>();
        context.Database.DatabaseNamespace.DatabaseName.Should().Be("flight_db");
    }

    [Fact]
    public async Task two_modules_get_separate_databases()
    {
        var builder = CreateBuilder(
            ("MongoOptions:Flight:ConnectionString", "mongodb://module"),
            ("MongoOptions:Flight:DatabaseName", "flight_db"),
            ("MongoOptions:Passenger:ConnectionString", "mongodb://module"),
            ("MongoOptions:Passenger:DatabaseName", "passenger_db")
        );

        builder.AddMongoDbContext<PersistMongoDbContext>("Flight");
        builder.AddMongoDbContext<PersistMongoDbContextB>("Passenger");

        await using var scope = builder.Services.BuildServiceProvider().CreateAsyncScope();
        scope
            .ServiceProvider.GetRequiredService<PersistMongoDbContext>()
            .Database.DatabaseNamespace.DatabaseName.Should()
            .Be("flight_db");
        scope
            .ServiceProvider.GetRequiredService<PersistMongoDbContextB>()
            .Database.DatabaseNamespace.DatabaseName.Should()
            .Be("passenger_db");
    }

    [Fact]
    public void module_registration_adds_unit_of_work_but_no_unqualified_context_or_repository()
    {
        var builder = CreateBuilder(
            ("MongoOptions:Flight:ConnectionString", "mongodb://module"),
            ("MongoOptions:Flight:DatabaseName", "flight_db")
        );

        builder.AddMongoDbContext<PersistMongoDbContext>("Flight");

        builder.Services.Should().Contain(d => d.ServiceType == typeof(IMongoUnitOfWork<>));
        builder.Services.Should().NotContain(d => d.ServiceType == typeof(IMongoDbContext));
        builder.Services.Should().NotContain(d => d.ServiceType == typeof(IMongoRepository<,>));
    }

    [Fact]
    public void legacy_registration_binds_options_and_registers_shared_abstractions()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                [
                    new KeyValuePair<string, string?>("MongoOptions:ConnectionString", "mongodb://root"),
                    new KeyValuePair<string, string?>("MongoOptions:DatabaseName", "root_db"),
                    new KeyValuePair<string, string?>("ConnectionStrings:mongo", "mongodb://aspire"),
                ]
            )
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);

        services.AddMongoDbContext<IPersistMongoServiceContext, PersistMongoServiceContext>(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<MongoOptions>>().Value;
        options.ConnectionString.Should().Be("mongodb://aspire");
        options.DatabaseName.Should().Be("root_db");
        services.Should().Contain(d => d.ServiceType == typeof(IMongoDbContext));
        services.Should().Contain(d => d.ServiceType == typeof(IMongoRepository<,>));
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMongoDbContext>().Should().BeOfType<PersistMongoServiceContext>();
    }
}
