using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using MassTransit;
using Xunit;

namespace Contracts.Messages.Tests;

// Enforces the backward-compatibility policy (src/Contracts/README.md): published messages may only change additively.
// Regenerate the baseline after an intentional additive change with UPDATE_CONTRACT_BASELINES=true.
public class MessageCompatibilityTests
{
    private const string BaselineFile = "messages.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void published_messages_change_only_additively()
    {
        var current = Snapshot();
        var sourceBaseline = Path.Combine(ContractTypes.RepoRoot, "src", "Contracts", "tests", "Contracts.Messages.Tests",
            "Baselines", BaselineFile);

        if (string.Equals(Environment.GetEnvironmentVariable("UPDATE_CONTRACT_BASELINES"), "true", StringComparison.OrdinalIgnoreCase))
        {
            File.WriteAllText(sourceBaseline, JsonSerializer.Serialize(current, JsonOptions) + Environment.NewLine);
            return;
        }

        var baseline = JsonSerializer.Deserialize<List<MessageShape>>(File.ReadAllText(sourceBaseline), JsonOptions)!;

        BreakingChanges(baseline, current).Should().BeEmpty("published messages must stay backward compatible; " +
            "add a new message version (e.g. a V2 type) for breaking changes");
        current.Select(m => m.Type).Should().BeEquivalentTo(baseline.Select(m => m.Type),
            "every message must be recorded in Baselines/{0} (run with UPDATE_CONTRACT_BASELINES=true after an additive change)",
            BaselineFile);
        current.Should().BeEquivalentTo(baseline, o => o.WithStrictOrdering(),
            "additive changes must be recorded in the baseline (run with UPDATE_CONTRACT_BASELINES=true)");
    }

    [Fact]
    public void compatibility_check_flags_removed_and_retyped_members_but_allows_additions()
    {
        var baseline = new List<MessageShape>
        {
            new("Ns.Msg", "urn:message:Ns:Msg", [new("Id", "System.Guid"), new("Name", "System.String")]),
            new("Ns.Gone", "urn:message:Ns:Gone", [new("Id", "System.Guid")]),
        };
        var current = new List<MessageShape>
        {
            new("Ns.Msg", "urn:message:Ns:Msg", [new("Id", "System.String"), new("Extra", "System.Int32")]),
        };

        BreakingChanges(baseline, current).Should().BeEquivalentTo(
            "Ns.Gone: message removed",
            "Ns.Msg.Id: type changed System.Guid -> System.String",
            "Ns.Msg.Name: member removed");

        BreakingChanges(baseline.Take(1).ToList(),
                [new("Ns.Msg", "urn:message:Ns:Msg", [new("Id", "System.Guid"), new("Name", "System.String"), new("Extra", "System.Int32")])])
            .Should().BeEmpty();
    }

    [Fact]
    public void message_urns_keep_the_original_namespace()
    {
        Snapshot().Should().OnlyContain(m => m.Urn.StartsWith("urn:message:BuildingBlocks.Contracts.EventBus.Messages:", StringComparison.Ordinal));
    }

    private static List<MessageShape> Snapshot() =>
        ContractTypes.Messages.Select(t => new MessageShape(
                t.FullName!,
                MessageUrn.ForTypeString(t),
                t.GetProperties().Where(p => p.DeclaringType == t)
                    .OrderBy(p => p.MetadataToken)
                    .Select(p => new MemberShape(p.Name, TypeName(p.PropertyType)))
                    .ToList()))
            .ToList();

    private static string TypeName(Type type) =>
        Nullable.GetUnderlyingType(type) is { } inner ? TypeName(inner) + "?" : type.FullName ?? type.Name;

    private static List<string> BreakingChanges(IReadOnlyList<MessageShape> baseline, IReadOnlyList<MessageShape> current)
    {
        var changes = new List<string>();
        foreach (var old in baseline)
        {
            var now = current.SingleOrDefault(m => m.Type == old.Type);
            if (now is null)
            {
                changes.Add($"{old.Type}: message removed");
                continue;
            }

            if (now.Urn != old.Urn)
                changes.Add($"{old.Type}: URN changed {old.Urn} -> {now.Urn}");

            foreach (var member in old.Members)
            {
                var match = now.Members.SingleOrDefault(m => m.Name == member.Name);
                if (match is null)
                    changes.Add($"{old.Type}.{member.Name}: member removed");
                else if (match.Type != member.Type)
                    changes.Add($"{old.Type}.{member.Name}: type changed {member.Type} -> {match.Type}");
            }
        }

        return changes.Order(StringComparer.Ordinal).ToList();
    }

    public sealed record MessageShape(string Type, string Urn, List<MemberShape> Members);

    public sealed record MemberShape(string Name, string Type);
}
