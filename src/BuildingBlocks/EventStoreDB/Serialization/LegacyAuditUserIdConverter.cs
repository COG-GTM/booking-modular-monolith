using Newtonsoft.Json;

namespace BuildingBlocks.EventStoreDB.Serialization;

// Events written before audit user ids became Guids stored CreatedBy/LastModifiedBy as the number 0.
public class LegacyAuditUserIdConverter : JsonConverter<Guid?>
{
    public override Guid? ReadJson(JsonReader reader, Type objectType, Guid? existingValue, bool hasExistingValue,
        JsonSerializer serializer) =>
        reader.TokenType switch
        {
            JsonToken.String when Guid.TryParse((string?)reader.Value, out var id) => id,
            _ => null
        };

    public override void WriteJson(JsonWriter writer, Guid? value, JsonSerializer serializer)
    {
        if (value is null)
            writer.WriteNull();
        else
            writer.WriteValue(value.Value);
    }
}
