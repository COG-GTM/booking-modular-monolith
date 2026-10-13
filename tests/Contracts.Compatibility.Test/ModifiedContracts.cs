// Deliberately modified copies of real contracts used by BreakingChangeDetectionTests.
namespace Contracts.Compatibility.Test.Additive
{
    public record UserCreated(Guid Id, string Name, string PassportNumber, string? Email = null);
}

namespace Contracts.Compatibility.Test.Defaulted
{
    public record UserCreated(Guid Id, string Name, string PassportNumber, string Country = "US");
}

namespace Contracts.Compatibility.Test.UnusedDefault
{
    public class UserCreated
    {
        public UserCreated() { }

        public UserCreated(Guid id, string name, string passportNumber, string country = "US")
        {
            Id = id;
            Name = name;
            PassportNumber = passportNumber;
            Country = country;
        }

        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string PassportNumber { get; init; } = string.Empty;
        public string Country { get; init; }
    }
}

namespace Contracts.Compatibility.Test.InitializerDefault
{
    public class UserCreated
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string PassportNumber { get; init; } = string.Empty;
        public string Country { get; init; } = "US";
    }
}

namespace Contracts.Compatibility.Test.PrivateJsonConstructor
{
    public class UserCreated
    {
        public UserCreated(Guid id)
            : this(id, string.Empty, string.Empty) { }

        public UserCreated(Guid id, string name)
            : this(id, name, string.Empty) { }

        [System.Text.Json.Serialization.JsonConstructor]
        private UserCreated(Guid id, string name, string passportNumber, string country = "US")
        {
            Id = id;
            Name = name;
            PassportNumber = passportNumber;
            Country = country;
        }

        public Guid Id { get; }
        public string Name { get; }
        public string PassportNumber { get; }
        public string Country { get; }
    }
}

namespace Contracts.Compatibility.Test.Breaking
{
    public record UserCreated(string Id, string? Name, string Passport);
}
