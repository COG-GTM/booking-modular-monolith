using Xunit;

namespace Contracts.Messages.Test;

public sealed class MessageCompatibilityTests
{
    private static readonly ContractShape[] Baseline =
    [
        new ContractShape(
            "Contract.UserCreated",
            "urn:message:Contract:UserCreated",
            [new PropertyShape("Id", "System.Guid"), new PropertyShape("Name", "System.String")]
        ),
    ];

    [Fact]
    public void Reports_removed_type()
    {
        Assert.Contains("Type removed: Contract.UserCreated", MessageCompatibility.Check(Baseline, []));
    }

    [Fact]
    public void Reports_changed_urn()
    {
        var current = new[] { Baseline[0] with { Urn = "urn:message:Contract:UserCreatedV2" } };

        Assert.Contains(
            MessageCompatibility.Check(Baseline, current),
            violation => violation.Contains("URN changed", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Reports_removed_property()
    {
        var current = new[] { Baseline[0] with { Properties = [new PropertyShape("Id", "System.Guid")] } };

        Assert.Contains("Property removed: Contract.UserCreated.Name", MessageCompatibility.Check(Baseline, current));
    }

    [Fact]
    public void Reports_changed_property_type()
    {
        var current = new[]
        {
            Baseline[0] with
            {
                Properties = [new PropertyShape("Id", "System.String"), Baseline[0].Properties[1]],
            },
        };

        Assert.Contains(
            MessageCompatibility.Check(Baseline, current),
            violation =>
                violation.Contains("Contract.UserCreated.Id", StringComparison.Ordinal)
                && violation.Contains("type changed", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Allows_added_property()
    {
        var current = new[]
        {
            Baseline[0] with
            {
                Properties = [.. Baseline[0].Properties, new PropertyShape("Email", "System.String")],
            },
        };

        Assert.Empty(MessageCompatibility.Check(Baseline, current));
    }
}
