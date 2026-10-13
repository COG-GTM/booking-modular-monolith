namespace Contracts.Compatibility.Test;

using Contracts.Compatibility.Test.Model;
using FluentAssertions;
using Xunit;

// Descriptor-based equivalent of `buf breaking --against <baseline>` for Contracts.Grpc. The
// descriptors come from the compiled Grpc.Tools output, so they are exactly what goes on the wire.
public class GrpcContractTests
{
    private static readonly IReadOnlyDictionary<string, ProtoFileSnapshot> Current = ContractSnapshots
        .ProtoFiles(ContractSnapshots.GrpcAssembly)
        .Select(ContractSnapshots.ForProto)
        .ToDictionary(file => Baselines.ProtoFile(file.File), StringComparer.Ordinal);

    [Fact]
    public void contracts_grpc_exposes_the_flight_and_passenger_protos()
    {
        Current.Keys.Should().Contain(["grpc/flight.json", "grpc/passenger.json"]);
    }

    public static TheoryData<string> ProtoBaselines()
    {
        var data = new TheoryData<string>();
        foreach (var file in Current.Keys.Union(Baselines.ExistingProtoBaselines()).Order(StringComparer.Ordinal))
        {
            data.Add(file);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ProtoBaselines))]
    public void proto_is_backward_compatible_with_baseline(string baselineFile)
    {
        var baseline = Baselines.Read<ProtoFileSnapshot>(baselineFile);
        Current.TryGetValue(baselineFile, out var current);

        if (baseline is null && current is not null && Baselines.UpdateRequested)
        {
            Baselines.Write(baselineFile, Baselines.Serialize(current));
            return;
        }

        baseline.Should().NotBeNull($"run with {Baselines.UpdateVariable}=true once to create {baselineFile}");
        current.Should().NotBeNull($"{baseline!.File} was removed from Contracts.Grpc");
        CompatibilityRules
            .Compare(baseline, current!)
            .Should()
            .BeEmpty("breaking proto changes must ship as a new versioned package/service (ADR 0006)");
    }

    [Theory]
    [MemberData(nameof(ProtoBaselines))]
    public void proto_baseline_is_up_to_date(string baselineFile)
    {
        if (!Current.TryGetValue(baselineFile, out var current))
        {
            return;
        }

        var expected = Baselines.Serialize(current);
        var committed = Baselines.ReadText(baselineFile);
        if (committed != expected && Baselines.UpdateRequested)
        {
            var baseline = Baselines.Read<ProtoFileSnapshot>(baselineFile);
            if (baseline is null || CompatibilityRules.Compare(baseline, current).Count == 0)
            {
                Baselines.Write(baselineFile, expected);
                return;
            }
        }

        committed
            .Should()
            .Be(
                expected,
                $"additive proto changes need a baseline refresh: {Baselines.UpdateVariable}=true dotnet test"
            );
    }
}
