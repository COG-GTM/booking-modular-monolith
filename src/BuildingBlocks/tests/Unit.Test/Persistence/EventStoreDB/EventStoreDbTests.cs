using System.Text;
using BuildingBlocks.Core.Event;
using BuildingBlocks.EventStoreDB;
using BuildingBlocks.EventStoreDB.Events;
using BuildingBlocks.EventStoreDB.Projections;
using BuildingBlocks.EventStoreDB.Repository;
using BuildingBlocks.EventStoreDB.Serialization;
using BuildingBlocks.EventStoreDB.Subscriptions;
using BuildingBlocks.Exception;
using EventStore.Client;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;

namespace BuildingBlocks.Unit.Test.Persistence;

public class StreamNameMapperTests
{
    [Fact]
    public void to_stream_id_generic_uses_type_name_and_id()
    {
        var id = Guid.NewGuid();

        StreamNameMapper.ToStreamId<EsdbTestAggregate>(id).Should().Be($"EsdbTestAggregate-{id}");
    }

    [Fact]
    public void to_stream_id_with_tenant_prefixes_stream_name()
    {
        var id = Guid.NewGuid();

        StreamNameMapper
            .ToStreamId(typeof(EsdbTestAggregate), id, "tenant1")
            .Should()
            .Be($"tenant1_EsdbTestAggregate-{id}");
    }
}

public class EventTypeMapperTests
{
    [Fact]
    public void to_name_replaces_dots_with_underscores()
    {
        EventTypeMapper.ToName<EsdbItemCreated>().Should().Be("BuildingBlocks_Unit_Test_Persistence_EsdbItemCreated");
    }

    [Fact]
    public void to_type_round_trips_a_mapped_name()
    {
        var name = EventTypeMapper.ToName<EsdbItemRenamed>();

        EventTypeMapper.ToType(name).Should().Be(typeof(EsdbItemRenamed));
    }

    [Fact]
    public void add_custom_map_works_in_both_directions()
    {
        EventTypeMapper.AddCustomMap<EsdbCustomMappedEvent>("esdbCustom");

        EventTypeMapper.ToName<EsdbCustomMappedEvent>().Should().Be("esdbCustom");
        EventTypeMapper.ToType("esdbCustom").Should().Be(typeof(EsdbCustomMappedEvent));
    }

    [Fact]
    public void to_type_returns_null_for_unknown_event_name()
    {
        EventTypeMapper.ToType("No_Such_Type_Exists_Anywhere").Should().BeNull();
    }
}

public class EventStoreDbSerializerTests
{
    private static ResolvedEvent CreateResolvedEvent(object @event, ulong revision = 3, ulong commitPosition = 42)
    {
        var record = new EventRecord(
            "EsdbTestAggregate-1",
            Uuid.NewUuid(),
            new StreamPosition(revision),
            new Position(commitPosition, commitPosition),
            new Dictionary<string, string>
            {
                ["type"] = EventTypeMapper.ToName(@event.GetType()),
                ["created"] = DateTime.UtcNow.Ticks.ToString(),
                ["content-type"] = "application/json",
            },
            Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(@event)),
            ReadOnlyMemory<byte>.Empty
        );
        return new ResolvedEvent(record, null, commitPosition);
    }

    [Fact]
    public void to_json_event_data_uses_mapped_type_name_and_json_payload()
    {
        var @event = new EsdbItemCreated(Guid.NewGuid(), "alpha");

        var data = @event.ToJsonEventData();

        data.Type.Should().Be(EventTypeMapper.ToName<EsdbItemCreated>());
        Encoding.UTF8.GetString(data.Data.Span).Should().Be(JsonConvert.SerializeObject(@event));
        Encoding.UTF8.GetString(data.Metadata.Span).Should().Be("{}");
    }

    [Fact]
    public void deserialize_returns_typed_event()
    {
        var @event = new EsdbItemCreated(Guid.NewGuid(), "alpha");
        var resolved = CreateResolvedEvent(@event);

        var deserialized = resolved.Deserialize<EsdbItemCreated>();
        deserialized.Should().NotBeNull();
        deserialized!.AggregateId.Should().Be(@event.AggregateId);
        deserialized.Name.Should().Be("alpha");
    }

    [Fact]
    public void deserialize_returns_null_for_unknown_type()
    {
        var record = new EventRecord(
            "stream-1",
            Uuid.NewUuid(),
            new StreamPosition(0),
            new Position(1, 1),
            new Dictionary<string, string>
            {
                ["type"] = "Unknown_Type_Xyz",
                ["created"] = "1",
                ["content-type"] = "application/json",
            },
            Encoding.UTF8.GetBytes("{}"),
            ReadOnlyMemory<byte>.Empty
        );

        new ResolvedEvent(record, null, 1).Deserialize().Should().BeNull();
    }

    [Fact]
    public void to_stream_event_carries_revision_and_commit_position()
    {
        var @event = new EsdbItemCreated(Guid.NewGuid(), "alpha");
        var resolved = CreateResolvedEvent(@event, revision: 7, commitPosition: 99);

        var streamEvent = resolved.ToStreamEvent();

        streamEvent.Should().BeOfType<StreamEvent<EsdbItemCreated>>();
        ((StreamEvent<EsdbItemCreated>)streamEvent!).Data.AggregateId.Should().Be(@event.AggregateId);
        ((StreamEvent<EsdbItemCreated>)streamEvent!).Data.Name.Should().Be("alpha");
        streamEvent!.Metadata.StreamRevision.Should().Be(7);
        streamEvent.Metadata.LogPosition.Should().Be(99);
    }
}

public class ContractResolverTests
{
    [Fact]
    public void private_setter_type_deserializes_via_most_specific_ctor()
    {
        var json = """{"Id":"11111111-1111-1111-1111-111111111111","Name":"alpha"}""";

        var back = json.FromJson<EsdbPrivateSetEvent>();

        back.Id.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        back.Name.Should().Be("alpha");
    }

    [Fact]
    public void json_constructor_attribute_wins_over_most_specific_ctor()
    {
        var json = """{"Value":"picked"}""";

        var back = json.FromJson<EsdbTwoCtorPoco>();

        back.Value.Should().Be("picked");
        back.Marker.Should().Be("attribute");
    }

    [Fact]
    public void two_json_constructor_attributes_throw()
    {
        var act = () => """{"Value":"x"}""".FromJson<EsdbDuplicateCtorPoco>();

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void from_json_with_type_and_to_json_round_trip()
    {
        var poco = new EsdbPrivateSetEvent(Guid.NewGuid(), "n");
        var json = poco.ToJson();

        var back = (EsdbPrivateSetEvent)json.FromJson(typeof(EsdbPrivateSetEvent));
        back.Id.Should().Be(poco.Id);
        back.Name.Should().Be(poco.Name);
    }

    [Fact]
    public void with_defaults_serializes_enums_as_strings()
    {
        var settings = new JsonSerializerSettings().WithDefaults();
        var json = JsonConvert.SerializeObject(new { Status = MessageStatusProxy.Processed }, settings);

        json.Should().Contain("\"Processed\"");
    }

    [Fact]
    public void to_json_string_content_sets_json_media_type()
    {
        var content = new { A = 1 }.ToJsonStringContent();

        content.Headers.ContentType!.MediaType.Should().Be("application/json");
        content.ReadAsStringAsync().Result.Should().Be("{\"A\":1}");
    }
}

public enum MessageStatusProxy
{
    InProgress = 1,
    Processed = 2,
}

public sealed class EsdbTwoCtorPoco
{
    public EsdbTwoCtorPoco(string value, string marker)
    {
        Value = value;
        Marker = marker;
    }

    [Newtonsoft.Json.JsonConstructor]
    public EsdbTwoCtorPoco(string value)
    {
        Value = value;
        Marker = "attribute";
    }

    public string Value { get; }
    public string Marker { get; }
}

public sealed class EsdbDuplicateCtorPoco
{
    [Newtonsoft.Json.JsonConstructor]
    public EsdbDuplicateCtorPoco(string value)
    {
        Value = value;
    }

    [Newtonsoft.Json.JsonConstructor]
    public EsdbDuplicateCtorPoco(string value, int other)
    {
        Value = value;
    }

    public string Value { get; }
}

public class ProjectionAndRepositoryTests
{
    [Fact]
    public async Task projection_publisher_invokes_all_registered_processors()
    {
        var recorder1 = new EsdbRecordingProjection();
        var recorder2 = new EsdbRecordingProjection();
        var services = new ServiceCollection()
            .AddSingleton<IProjectionProcessor>(recorder1)
            .AddSingleton<IProjectionProcessor>(recorder2)
            .BuildServiceProvider();
        var publisher = new ProjectionPublisher(services);
        var @event = new EsdbItemCreated(Guid.NewGuid(), "x");
        var streamEvent = new StreamEvent<EsdbItemCreated>(@event, new EventMetadata(1, 2));

        await publisher.PublishAsync((StreamEvent)streamEvent);

        recorder1.Events.Should().ContainSingle();
        recorder2.Events.Should().ContainSingle();
        ((StreamEvent<EsdbItemCreated>)recorder1.Events[0]).Data.Should().Be(@event);
    }

    [Fact]
    public async Task in_memory_checkpoint_repository_stores_and_overwrites()
    {
        var repository = new InMemorySubscriptionCheckpointRepository();

        (await repository.Load("sub", CancellationToken.None)).Should().BeNull();

        await repository.Store("sub", 5, CancellationToken.None);
        (await repository.Load("sub", CancellationToken.None)).Should().Be(5);

        await repository.Store("sub", 9, CancellationToken.None);
        (await repository.Load("sub", CancellationToken.None)).Should().Be(9);
    }

    [Fact]
    public async Task repository_get_throws_when_find_returns_null()
    {
        var repository = Substitute.For<IEventStoreDBRepository<EsdbTestAggregate>>();
        repository.Find(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((EsdbTestAggregate?)null);

        var act = () => repository.Get(Guid.NewGuid(), CancellationToken.None);

        await act.Should().ThrowAsync<AggregateNotFoundException>();
    }

    [Fact]
    public async Task repository_get_and_update_applies_action_then_updates()
    {
        var aggregate = new EsdbTestAggregate();
        var repository = Substitute.For<IEventStoreDBRepository<EsdbTestAggregate>>();
        repository.Find(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(aggregate);
        repository.Update(aggregate, 5L, Arg.Any<CancellationToken>()).Returns(7UL);

        var result = await repository.GetAndUpdate(
            Guid.NewGuid(),
            a => a.Create(Guid.NewGuid(), "new"),
            expectedVersion: 5
        );

        aggregate.Name.Should().Be("new");
        result.Should().Be(7UL);
        await repository.Received(1).Update(aggregate, 5L, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void aggregate_event_sourcing_collects_and_clears_events()
    {
        var aggregate = new EsdbTestAggregate();

        aggregate.Create(Guid.NewGuid(), "a");
        aggregate.Rename("b");
        aggregate.DomainEvents.Should().HaveCount(2);

        var cleared = aggregate.ClearDomainEvents();
        cleared.Should().HaveCount(2);
        aggregate.DomainEvents.Should().BeEmpty();
    }
}

public class EventStoreDbConfigTests
{
    private static IConfiguration EmptyConfig() => new ConfigurationBuilder().Build();

    [Fact]
    public void add_event_store_db_registers_repository_subscription_and_checkpoint()
    {
        var services = new ServiceCollection();

        services.AddEventStoreDB(EmptyConfig());

        services.Should().Contain(d => d.ServiceType == typeof(IEventStoreDBRepository<>));
        services.Should().Contain(d => d.ServiceType == typeof(EventStoreDBSubscriptionToAll));
        services.Should().Contain(d => d.ServiceType == typeof(ISubscriptionCheckpointRepository));
    }

    [Fact]
    public void add_event_store_db_skips_checkpoint_repo_when_disabled()
    {
        var services = new ServiceCollection();

        services.AddEventStoreDB(EmptyConfig(), new EventStoreDBOptions(UseInternalCheckpointing: false));

        services.Should().NotContain(d => d.ServiceType == typeof(ISubscriptionCheckpointRepository));
    }

    [Fact]
    public void add_event_store_db_subscription_to_all_registers_hosted_service()
    {
        var services = new ServiceCollection();

        services.AddEventStoreDBSubscriptionToAll();

        services.Should().Contain(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService));
        services.Should().Contain(d => d.ServiceType == typeof(ISubscriptionCheckpointRepository));
    }

    [Fact]
    public void add_projections_registers_publisher_and_scans_processors()
    {
        var services = new ServiceCollection();

        services.AddProjections(typeof(EventStoreDbConfigTests).Assembly);

        services.Should().Contain(d => d.ServiceType == typeof(IProjectionPublisher));
        services
            .Should()
            .Contain(d =>
                d.ServiceType == typeof(IProjectionProcessor) && d.ImplementationType == typeof(EsdbRecordingProjection)
            );
    }
}
