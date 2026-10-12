namespace Contracts.Test;

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Booking;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core.Event;
using Contracts.Grpc.Flight.V1;
using Contracts.Grpc.Passenger.V1;
using Contracts.Test.ContractModel;
using Flight;
using FluentAssertions;
using Identity;
using Passenger;
using Xunit;

public class ContractCompatibilityTests
{
    private static readonly IReadOnlyDictionary<string, GrpcSnapshot> GrpcSnapshots =
        ContractSnapshotFactory.CreateGrpcSnapshots(typeof(FlightReflection).Assembly);
    private static readonly MessagesSnapshot MessageSnapshot = ContractSnapshotFactory.CreateMessagesSnapshot(
        typeof(UserCreated).Assembly
    );

    public static IEnumerable<object[]> GrpcPackageData() =>
        GrpcSnapshots
            .Keys.Concat(
                Directory.Exists(Path.Combine(BaselineFiles.OutputDirectory, "grpc"))
                    ? Directory
                        .GetFiles(Path.Combine(BaselineFiles.OutputDirectory, "grpc"), "*.json")
                        .Select(path => Path.GetFileNameWithoutExtension(path))
                    : []
            )
            .Distinct(StringComparer.Ordinal)
            .Select(package => new object[] { package, GrpcSnapshots.GetValueOrDefault(package) });

    [Theory]
    [MemberData(nameof(GrpcPackageData))]
    public void current_contracts_are_backward_compatible_with_baseline(string package, GrpcSnapshot? current)
    {
        var baseline = BaselineFiles.Read<GrpcSnapshot>($"grpc/{package}.json");
        if (current is null)
        {
            baseline.Should().BeNull($"gRPC package {package} has not been removed");
            return;
        }

        if (baseline is null && UpdateRequested())
        {
            BaselineFiles.Write($"grpc/{package}.json", current);
            return;
        }

        baseline
            .Should()
            .NotBeNull($"baseline {package} must exist; run with UPDATE_CONTRACT_BASELINES=true to create it");
        CompatibilityChecker
            .Compare(baseline!, current)
            .Should()
            .BeEmpty(
                $"gRPC contract {package} remains backward compatible:{Environment.NewLine}{string.Join(Environment.NewLine, CompatibilityChecker.Compare(baseline!, current))}"
            );
    }

    [Fact]
    public void current_messages_are_backward_compatible_with_baseline()
    {
        var baseline = BaselineFiles.Read<MessagesSnapshot>("messages/contracts.messages.v1.json");
        if (baseline is null && UpdateRequested())
        {
            UpdateMessageBaselineIfRequested();
            return;
        }

        baseline
            .Should()
            .NotBeNull("message baseline must exist; run with UPDATE_CONTRACT_BASELINES=true to create it");
        CompatibilityChecker
            .Compare(baseline!, MessageSnapshot)
            .Should()
            .BeEmpty(
                $"message contracts remain backward compatible:{Environment.NewLine}{string.Join(Environment.NewLine, CompatibilityChecker.Compare(baseline!, MessageSnapshot))}"
            );
    }

    [Theory]
    [MemberData(nameof(GrpcPackageData))]
    public void baseline_is_up_to_date(string package, GrpcSnapshot? current)
    {
        if (current is null)
        {
            BaselineFiles
                .Read<GrpcSnapshot>($"grpc/{package}.json")
                .Should()
                .BeNull($"gRPC package {package} has not been removed");
            return;
        }

        UpdateGrpcBaselineIfRequested(package, current);
        AssertBaselineUpToDate($"grpc/{package}.json", current, CompatibilityChecker.Compare);
    }

    [Fact]
    public void message_baseline_is_up_to_date()
    {
        if (UpdateRequested())
        {
            UpdateMessageBaselineIfRequested();
        }

        AssertBaselineUpToDate("messages/contracts.messages.v1.json", MessageSnapshot, CompatibilityChecker.Compare);
    }

    [Fact]
    public void golden_samples_deserialize_into_current_contracts()
    {
        var baseline = BaselineFiles.Read<MessagesSnapshot>("messages/contracts.messages.v1.json");
        baseline.Should().NotBeNull();
        var options = MassTransit.Serialization.SystemTextJsonMessageSerializer.Options;
        foreach (var message in baseline!.Messages)
        {
            var type = typeof(UserCreated).Assembly.GetType(message.Key);
            type.Should().NotBeNull($"the current assembly contains {message.Key}");
            var samplePath = Path.Combine(BaselineFiles.OutputDirectory, "messages", "samples", $"{type!.Name}.json");
            File.Exists(samplePath).Should().BeTrue($"golden sample exists for {type.Name}");
            var json = File.ReadAllText(samplePath);
            var instance = JsonSerializer.Deserialize(json, type, options);
            instance.Should().NotBeNull();
            using var sampleDocument = JsonDocument.Parse(json);
            foreach (var property in message.Value.Properties)
            {
                var currentProperty = type.GetProperty(property.Key);
                currentProperty.Should().NotBeNull();
                var value = currentProperty!.GetValue(instance);
                var samplePropertyName = JsonNamingPolicy.CamelCase.ConvertName(property.Key);
                var hasSampleValue =
                    sampleDocument.RootElement.TryGetProperty(samplePropertyName, out var sampleValue)
                    || sampleDocument.RootElement.TryGetProperty(property.Key, out sampleValue);
                hasSampleValue.Should().BeTrue($"the sample contains {property.Key}");
                sampleValue.ValueKind.Should().NotBe(JsonValueKind.Null);
                JsonSerializer
                    .Serialize(value, options)
                    .Should()
                    .Be(
                        JsonSerializer.Serialize(
                            JsonSerializer.Deserialize(sampleValue.GetRawText(), currentProperty.PropertyType, options),
                            options
                        ),
                        $"baseline property {message.Key}.{property.Key} is populated by the sample"
                    );
            }
        }
    }

    [Fact]
    public void grpc_consumer_pacts_are_satisfied_by_current_contracts()
    {
        foreach (var pact in PactContracts.ReadGrpcPacts())
        {
            GrpcSnapshots.Should().ContainKey(pact.Package);
            PactContracts
                .Validate(pact, GrpcSnapshots[pact.Package])
                .Should()
                .BeEmpty(
                    $"the {pact.Consumer} pact is satisfied:{Environment.NewLine}{string.Join(Environment.NewLine, PactContracts.Validate(pact, GrpcSnapshots[pact.Package]))}"
                );
        }
    }

    [Fact]
    public void message_consumer_pacts_are_satisfied_by_current_contracts()
    {
        foreach (var pact in PactContracts.ReadMessagePacts())
        {
            PactContracts
                .Validate(pact, MessageSnapshot)
                .Should()
                .BeEmpty(
                    $"the {pact.Consumer} pact is satisfied:{Environment.NewLine}{string.Join(Environment.NewLine, PactContracts.Validate(pact, MessageSnapshot))}"
                );
        }
    }

    [Fact]
    public void every_module_consumer_has_a_pact()
    {
        var moduleAssemblies = new[]
        {
            typeof(IdentityRoot).Assembly,
            typeof(FlightRoot).Assembly,
            typeof(PassengerRoot).Assembly,
            typeof(BookingRoot).Assembly,
        };
        var contractNames = MessageSnapshot.Messages.Keys.ToHashSet(StringComparer.Ordinal);
        var discovered = PactContracts.DiscoverMessageConsumers(moduleAssemblies, contractNames);
        var declared = PactContracts
            .ReadMessagePacts()
            .Select(pact => $"{pact.Consumer}|{pact.MessageType}")
            .ToHashSet(StringComparer.Ordinal);
        discovered.Except(declared).Should().BeEmpty("every contract consumer must declare a pact");
    }

    private static void AssertBaselineUpToDate<T>(string path, T current, Func<T, T, IReadOnlyList<string>> compare)
        where T : class
    {
        var baseline = BaselineFiles.Read<T>(path);
        if (baseline is null)
        {
            if (UpdateRequested())
            {
                BaselineFiles.Write(path, current);
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"Missing contract baseline {path}; run with UPDATE_CONTRACT_BASELINES=true."
            );
        }

        var breakingChanges = compare(baseline, current);
        if (breakingChanges.Count > 0)
        {
            throw new Xunit.Sdk.XunitException(
                $"Breaking contract changes cannot update the baseline automatically:{Environment.NewLine}{string.Join(Environment.NewLine, breakingChanges)}"
            );
        }

        if (UpdateRequested())
        {
            BaselineFiles.Write(path, current);
            return;
        }

        string.Equals(
                ContractSnapshotFactory.Serialize(current),
                ContractSnapshotFactory.Serialize(baseline),
                StringComparison.Ordinal
            )
            .Should()
            .BeTrue($"baseline {path} includes all current contract elements; run with UPDATE_CONTRACT_BASELINES=true");
    }

    private static void UpdateGrpcBaselineIfRequested(string package, GrpcSnapshot current)
    {
        if (UpdateRequested())
        {
            var existing = BaselineFiles.Read<GrpcSnapshot>($"grpc/{package}.json");
            if (existing is not null)
            {
                CompatibilityChecker
                    .Compare(existing, current)
                    .Should()
                    .BeEmpty("breaking baselines require a visible hand edit");
            }

            BaselineFiles.Write($"grpc/{package}.json", current);
        }
    }

    private static void UpdateMessageBaselineIfRequested()
    {
        var existing = BaselineFiles.Read<MessagesSnapshot>("messages/contracts.messages.v1.json");
        if (existing is not null)
        {
            CompatibilityChecker
                .Compare(existing, MessageSnapshot)
                .Should()
                .BeEmpty("breaking baselines require a visible hand edit");
        }

        BaselineFiles.Write("messages/contracts.messages.v1.json", MessageSnapshot);

        foreach (var type in BaselineFiles.ContractMessageTypes())
        {
            var relativePath = $"messages/samples/{type.Name}.json";
            var path = BaselineFiles.SourcePath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, BaselineFiles.SerializeSample(type) + Environment.NewLine);
        }
    }

    private static bool UpdateRequested() =>
        string.Equals(
            Environment.GetEnvironmentVariable("UPDATE_CONTRACT_BASELINES"),
            "true",
            StringComparison.OrdinalIgnoreCase
        );
}
