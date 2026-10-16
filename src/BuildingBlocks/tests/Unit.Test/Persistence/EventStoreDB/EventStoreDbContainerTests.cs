using BuildingBlocks.EventStoreDB.Repository;
using BuildingBlocks.EventStoreDB.Subscriptions;
using EventStore.Client;
using FluentAssertions;
using Testcontainers.EventStoreDb;
using Xunit;

namespace BuildingBlocks.Unit.Test.Persistence;

public class EventStoreDbContainerTests : IAsyncLifetime
{
    private readonly EventStoreDbContainer _eventStore = new EventStoreDbBuilder()
        .WithImage("eventstore/eventstore:latest")
        .Build();

    public Task InitializeAsync() => _eventStore.StartAsync();

    public Task DisposeAsync() => _eventStore.DisposeAsync().AsTask();

    private EventStoreClient CreateClient() => new(EventStoreClientSettings.Create(_eventStore.GetConnectionString()));

    [Fact]
    public async Task add_then_find_rebuilds_aggregate_via_when()
    {
        var client = CreateClient();
        var repository = new EventStoreDBRepository<EsdbTestAggregate>(client);
        var aggregate = new EsdbTestAggregate();
        var id = Guid.NewGuid();
        aggregate.Create(id, "alpha");

        var revision = await repository.Add(aggregate, CancellationToken.None);
        revision.Should().Be(0UL);

        var found = await repository.Find(id, CancellationToken.None);
        found.Should().NotBeNull();
        found!.Id.Should().Be(id);
        found.Name.Should().Be("alpha");
        found.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task update_with_correct_revision_appends()
    {
        var client = CreateClient();
        var repository = new EventStoreDBRepository<EsdbTestAggregate>(client);
        var aggregate = new EsdbTestAggregate();
        var id = Guid.NewGuid();
        aggregate.Create(id, "alpha");
        await repository.Add(aggregate, CancellationToken.None);
        aggregate.ClearDomainEvents();

        aggregate.Rename("beta");
        var revision = await repository.Update(aggregate, expectedRevision: 0);

        revision.Should().Be(1UL);
        (await repository.Find(id, CancellationToken.None))!.Name.Should().Be("beta");
    }

    [Fact]
    public async Task update_with_stale_revision_throws_wrong_expected_version()
    {
        var client = CreateClient();
        var repository = new EventStoreDBRepository<EsdbTestAggregate>(client);
        var aggregate = new EsdbTestAggregate();
        var id = Guid.NewGuid();
        aggregate.Create(id, "alpha");
        await repository.Add(aggregate, CancellationToken.None);
        aggregate.ClearDomainEvents();

        aggregate.Rename("beta");
        var act = () => repository.Update(aggregate, expectedRevision: 99);

        await act.Should().ThrowAsync<WrongExpectedVersionException>();
    }

    [Fact]
    public async Task checkpoint_repository_stores_loads_and_overwrites()
    {
        var client = CreateClient();
        var repository = new EventStoreDBSubscriptionCheckpointRepository(client);
        var subscriptionId = $"sub-{Guid.NewGuid():N}";

        (await repository.Load(subscriptionId, CancellationToken.None)).Should().BeNull();

        await repository.Store(subscriptionId, 10, CancellationToken.None);
        (await repository.Load(subscriptionId, CancellationToken.None)).Should().Be(10);

        await repository.Store(subscriptionId, 25, CancellationToken.None);
        (await repository.Load(subscriptionId, CancellationToken.None)).Should().Be(25);
    }
}
