// Deliberately modified copies of real contracts used by BreakingChangeDetectionTests.
namespace Contracts.Compatibility.Test.Additive
{
    public record UserCreated(Guid Id, string Name, string PassportNumber, string? Email = null);
}

namespace Contracts.Compatibility.Test.Breaking
{
    public record UserCreated(string Id, string? Name, string Passport);
}
