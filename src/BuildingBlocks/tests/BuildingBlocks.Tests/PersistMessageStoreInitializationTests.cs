using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace BuildingBlocks.Tests;

public class PersistMessageStoreInitializationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task resolving_module_store_initializes_empty_and_preexisting_databases_idempotently(
        bool existingTable
    )
    {
        await using var postgres = new PostgreSqlBuilder().WithImage("postgres:latest").Build();
        await postgres.StartAsync();
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "test", ContentRootPath = AppContext.BaseDirectory }
        );
        builder.Configuration["ConnectionStrings:first-persist-message"] = postgres.GetConnectionString();
        builder.AddPersistMessageProcessor<First>("First");
        await using var provider = builder.Services.BuildServiceProvider();
        if (existingTable)
        {
            await using var setup = provider.CreateAsyncScope();
            await setup
                .ServiceProvider.GetRequiredService<PersistMessageDbContext<First>>()
                .Database.ExecuteSqlRawAsync("CREATE TABLE other_module (id integer PRIMARY KEY)");
        }

        for (var i = 0; i < 2; i++)
        {
            await using var scope = provider.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IPersistMessageDbContext<First>>();
            store.Should().BeSameAs(scope.ServiceProvider.GetRequiredService<PersistMessageDbContext<First>>());
            (await store.PersistMessage.CountAsync()).Should().Be(0);
        }
    }

    public sealed class First;
}
