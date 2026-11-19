using BuildingBlocks.Core.Event;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using MassTransit;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit;

namespace BuildingBlocks.Tests;

public class InternalCommandStoreRoutingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task shared_fallback_worker_respects_the_command_modules_store_override(bool isolated)
    {
        await using var postgres = new PostgreSqlBuilder().WithImage("postgres:16").Build();
        await postgres.StartAsync();
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "test", ContentRootPath = AppContext.BaseDirectory }
        );
        var sharedConnection = postgres.GetConnectionString();
        builder.Configuration["ConnectionStrings:persist-message"] = sharedConnection;
        builder.Configuration["Modules:CommandModule:BackgroundProcessingEnabled"] = "false";
        if (isolated)
            builder.Configuration["PersistMessageOptions:ConnectionString:CommandModule"] =
                new NpgsqlConnectionStringBuilder(sharedConnection) { Database = "isolated_commands" }.ConnectionString;

        var mediator = Substitute.For<IMediator>();
        builder.Services.AddSingleton(mediator);
        builder.Services.AddSingleton(Substitute.For<IPublishEndpoint>());
        builder.AddPersistMessageProcessor<MessageEnvelope>("WorkerModule");
        builder.AddPersistMessageProcessor<InternalCommandStoreRoutingTests>("CommandModule");
        await using var provider = builder.Services.BuildServiceProvider();
        var command = new RoutedCommand(Guid.NewGuid());

        await using (var setup = provider.CreateAsyncScope())
        {
            var writer = setup.ServiceProvider.GetRequiredService<IPersistMessageProcessor<MessageEnvelope>>();
            await writer.AddInternalMessageAsync(command);
        }

        await using (var processing = provider.CreateAsyncScope())
        {
            var worker = processing.ServiceProvider.GetRequiredService<IPersistMessageProcessor<MessageEnvelope>>();
            await worker.ProcessAllAsync();
        }

        await using var verification = provider.CreateAsyncScope();
        var store = verification.ServiceProvider.GetRequiredService<IPersistMessageDbContext<MessageEnvelope>>();
        var row = await store.PersistMessage.AsNoTracking().SingleAsync();
        if (isolated)
        {
            mediator.ReceivedCalls().Should().BeEmpty();
            row.MessageStatus.Should().Be(MessageStatus.InProgress);
        }
        else
        {
            mediator.ReceivedCalls().Should().ContainSingle().Which.GetArguments()[0].Should().Be(command);
            row.MessageStatus.Should().Be(MessageStatus.Processed);
        }
    }

    public sealed record RoutedCommand(Guid Id) : InternalCommand;
}
