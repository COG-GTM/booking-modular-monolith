using BuildingBlocks.Core.Model;
using BuildingBlocks.EventStoreDB.Serialization;
using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Unit.Test.Audit;

public class LegacyAuditUserIdConverterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void audit_ids_should_round_trip_with_converter(bool anonymous)
    {
        var original = new AuditedEvent
        {
            CreatedBy = anonymous ? null : Guid.NewGuid(),
            LastModifiedBy = anonymous ? null : Guid.NewGuid(),
        };
        var converter = new LegacyAuditUserIdConverter();

        var json = JsonConvert.SerializeObject(original, converter);
        var restored = JsonConvert.DeserializeObject<AuditedEvent>(json, converter)!;

        restored.CreatedBy.Should().Be(original.CreatedBy);
        restored.LastModifiedBy.Should().Be(original.LastModifiedBy);
        var tokenType = anonymous ? JTokenType.Null : JTokenType.String;
        JObject.Parse(json)["CreatedBy"]!.Type.Should().Be(tokenType);
        JObject.Parse(json)["LastModifiedBy"]!.Type.Should().Be(tokenType);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("123")]
    [InlineData("\"invalid-guid\"")]
    public void unreadable_legacy_audit_ids_should_be_null(string value)
    {
        var json = $"{{\"CreatedBy\":{value},\"LastModifiedBy\":{value}}}";

        var restored = JsonConvert.DeserializeObject<AuditedEvent>(json, new LegacyAuditUserIdConverter())!;

        restored.CreatedBy.Should().BeNull();
        restored.LastModifiedBy.Should().BeNull();
    }

    private record AuditedEvent : Entity<long>;
}
