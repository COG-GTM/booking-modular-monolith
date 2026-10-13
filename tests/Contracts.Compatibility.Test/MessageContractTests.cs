namespace Contracts.Compatibility.Test;

using Contracts.Compatibility.Test.Model;
using FluentAssertions;
using Xunit;

public class MessageContractTests
{
    private static readonly MessageContractsSnapshot Current = ContractSnapshots.ForMessages(
        ContractSnapshots.MessagesAssembly
    );

    [Fact]
    public void contracts_assembly_exposes_integration_events()
    {
        Current.Messages.Should().NotBeEmpty();
        Current.Messages.Should().ContainKey("BuildingBlocks.Contracts.EventBus.Messages.UserCreated");
    }

    [Fact]
    public void integration_events_are_backward_compatible_with_baseline()
    {
        var baseline = Baselines.Read<MessageContractsSnapshot>(Baselines.MessagesFile);
        if (baseline is null && Baselines.UpdateRequested)
        {
            Baselines.Write(Baselines.MessagesFile, Baselines.Serialize(Current));
            return;
        }

        baseline
            .Should()
            .NotBeNull($"run with {Baselines.UpdateVariable}=true once to create {Baselines.MessagesFile}");
        CompatibilityRules
            .Compare(baseline!, Current)
            .Should()
            .BeEmpty("breaking changes to Contracts.Messages must ship as a new versioned message type (ADR 0006)");
    }

    // Additive changes are allowed but must be committed to the baseline so the next change is
    // compared against them; otherwise a later removal of the new member would go unnoticed.
    [Fact]
    public void baseline_is_up_to_date()
    {
        var expected = Baselines.Serialize(Current);
        var committed = Baselines.ReadText(Baselines.MessagesFile);
        if (committed != expected && Baselines.UpdateRequested)
        {
            var baseline = Baselines.Read<MessageContractsSnapshot>(Baselines.MessagesFile);
            if (baseline is null || CompatibilityRules.Compare(baseline, Current).Count == 0)
            {
                Baselines.Write(Baselines.MessagesFile, expected);
                return;
            }
        }

        committed
            .Should()
            .Be(
                expected,
                $"additive contract changes need a baseline refresh: {Baselines.UpdateVariable}=true dotnet test"
            );
    }

    public static TheoryData<string> MessageTypes()
    {
        var data = new TheoryData<string>();
        foreach (var name in Current.Messages.Keys)
        {
            data.Add(name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(MessageTypes))]
    public void golden_sample_deserializes_into_current_contract(string messageName)
    {
        var type = ContractSnapshots.MessagesAssembly.GetType(messageName)!;
        var sampleFile = Baselines.SampleFile(type);
        var sample = Baselines.ReadText(sampleFile);
        if (sample is null && Baselines.UpdateRequested)
        {
            Baselines.Write(sampleFile, GoldenSamples.Create(type));
            return;
        }

        sample.Should().NotBeNull($"every integration event needs a golden payload at Baselines/{sampleFile}");
        var baseline =
            Baselines.Read<MessageContractsSnapshot>(Baselines.MessagesFile)?.Messages.GetValueOrDefault(messageName)
            ?? Current.Messages[messageName];
        GoldenSamples.Verify(messageName, baseline, type, sample!).Should().BeEmpty();
    }
}
