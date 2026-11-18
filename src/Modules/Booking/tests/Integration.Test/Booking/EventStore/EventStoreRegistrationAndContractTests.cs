using System;
using Booking.Booking.ValueObjects;
using BuildingBlocks.Core.Event;
using BuildingBlocks.EventStoreDB.Events;
using BuildingBlocks.EventStoreDB.Serialization;
using BuildingBlocks.Utils;
using FluentAssertions;
using Newtonsoft.Json;
using Xunit;

namespace Integration.Test.Booking.EventStore;

public class EventStoreRegistrationAndContractTests
{
    [Fact]
    public void assembly_registration_should_only_register_concrete_closed_domain_events()
    {
        EventTypeMapper.AddDomainEventTypes(typeof(EventStoreRegistrationAndContractTests).Assembly);

        EventTypeMapper
            .ToType(EventName(typeof(AssemblyRegistrationDomainEventProbe)))
            .Should()
            .Be(typeof(AssemblyRegistrationDomainEventProbe));
        EventTypeMapper.ToType(EventName(typeof(AssemblyRegistrationAbstractDomainEventProbe))).Should().BeNull();
        EventTypeMapper.ToType(EventName(typeof(AssemblyRegistrationGenericDomainEventProbe<>))).Should().BeNull();
        EventTypeMapper.ToType(EventName(typeof(AssemblyRegistrationNonDomainEventProbe))).Should().BeNull();
    }

    [Fact]
    public void custom_map_should_resolve_a_previously_missing_custom_name()
    {
        const string name = "registration-contract-custom-event";
        EventTypeMapper.ToType(name).Should().BeNull();

        EventTypeMapper.AddCustomMap<CustomRegistrationProbe>(name);

        EventTypeMapper.ToType(name).Should().Be(typeof(CustomRegistrationProbe));
        EventTypeMapper.ToName<CustomRegistrationProbe>().Should().Be(name);
    }

    [Fact]
    public void to_name_should_register_a_previously_missing_name()
    {
        var name = EventName(typeof(ToNameRegistrationProbe));
        EventTypeMapper.ToType(name).Should().BeNull();

        EventTypeMapper.ToName<ToNameRegistrationProbe>().Should().Be(name);

        EventTypeMapper.ToType(name).Should().Be(typeof(ToNameRegistrationProbe));
    }

    [Fact]
    public void contract_resolver_should_use_public_constructor_with_most_parameters()
    {
        var result = "{\"name\":\"probe\",\"count\":7}".FromJson<MultiplePublicConstructorsProbe>();

        result.Name.Should().Be("probe");
        result.Count.Should().Be(7);
        result.ConstructorParameterCount.Should().Be(2);
    }

    [Fact]
    public void contract_resolver_should_use_private_json_constructor()
    {
        var result = "{\"name\":\"probe\"}".FromJson<PrivateJsonConstructorProbe>();

        result.Name.Should().Be("probe");
        result.UsedPrivateConstructor.Should().BeTrue();
    }

    [Fact]
    public void passenger_info_should_deserialize_standalone()
    {
        var result = "{\"name\":\"Mallory\"}".FromJson<PassengerInfo>();

        result.Should().Be(PassengerInfo.Of("Mallory"));
    }

    [Fact]
    public void trip_should_deserialize_standalone()
    {
        var expected = Trip.Of(
            "BZ1",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateTime(2026, 11, 18, 0, 0, 0, DateTimeKind.Utc),
            100m,
            "desc",
            "12C"
        );

        var result = expected.ToJson().FromJson<Trip>();

        result.Should().Be(expected);
    }

    [Fact]
    public void constrained_type_lookup_should_resolve_short_names()
    {
        TypeProvider
            .GetFirstMatchingTypeFromCurrentDomainAssembly<IEvent>(nameof(AssemblyRegistrationDomainEventProbe))
            .Should()
            .Be(typeof(AssemblyRegistrationDomainEventProbe));
    }

    [Theory]
    [InlineData(nameof(IEvent))]
    [InlineData(nameof(IDomainEvent))]
    [InlineData(nameof(AssemblyRegistrationAbstractDomainEventProbe))]
    public void constrained_type_lookup_should_reject_interfaces_and_abstract_classes(string name)
    {
        TypeProvider.GetFirstMatchingTypeFromCurrentDomainAssembly<IEvent>(name).Should().BeNull();
    }

    [Fact]
    public void constrained_type_lookup_should_resolve_internal_command_by_full_name()
    {
        TypeProvider
            .GetFirstMatchingTypeFromCurrentDomainAssembly<IInternalCommand>(
                typeof(InternalCommandLookupProbe).FullName!
            )
            .Should()
            .Be(typeof(InternalCommandLookupProbe));
    }

    private static string EventName(Type type) => type.FullName!.Replace(".", "_", StringComparison.CurrentCulture);

    private sealed class AssemblyRegistrationDomainEventProbe : IDomainEvent { }

    private abstract class AssemblyRegistrationAbstractDomainEventProbe : IDomainEvent { }

    private sealed class AssemblyRegistrationGenericDomainEventProbe<T> : IDomainEvent { }

    private sealed class AssemblyRegistrationNonDomainEventProbe : IEvent { }

    private sealed class CustomRegistrationProbe { }

    private sealed class ToNameRegistrationProbe { }

    private sealed class InternalCommandLookupProbe : IInternalCommand { }

    private sealed class MultiplePublicConstructorsProbe
    {
        public MultiplePublicConstructorsProbe(string name)
        {
            Name = name;
            ConstructorParameterCount = 1;
        }

        public MultiplePublicConstructorsProbe(string name, int count)
        {
            Name = name;
            Count = count;
            ConstructorParameterCount = 2;
        }

        public string Name { get; }
        public int Count { get; }
        public int ConstructorParameterCount { get; }
    }

    private sealed class PrivateJsonConstructorProbe
    {
        public PrivateJsonConstructorProbe()
        {
            Name = "public";
        }

        [JsonConstructor]
        private PrivateJsonConstructorProbe(string name)
        {
            Name = name;
            UsedPrivateConstructor = true;
        }

        public string Name { get; }
        public bool UsedPrivateConstructor { get; }
    }
}
