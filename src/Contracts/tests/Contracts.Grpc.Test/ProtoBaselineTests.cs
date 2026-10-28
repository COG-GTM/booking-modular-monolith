using System.Text.Json;
using Contracts.Grpc.Flight;
using Xunit;

namespace Contracts.Grpc.Test;

internal static class ProtoBaselineTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static ProtoFileSnapshot[] GetCurrentSnapshots() =>
        [
            ProtoFileSnapshot.FromDescriptor(FlightReflection.Descriptor.SerializedData),
            ProtoFileSnapshot.FromDescriptor(Contracts.Grpc.Passenger.PassengerReflection.Descriptor.SerializedData),
        ];

    public static string Serialize(IEnumerable<ProtoFileSnapshot> snapshots) =>
        JsonSerializer.Serialize(snapshots, JsonOptions);

    public static ProtoFileSnapshot[] ReadBaseline()
    {
        var baselineDirectory = Environment.GetEnvironmentVariable("CONTRACT_BASELINE_DIR");
        var baselinePath = string.IsNullOrWhiteSpace(baselineDirectory)
            ? Path.Combine(AppContext.BaseDirectory, "Baselines", "protos.json")
            : Path.Combine(baselineDirectory, "protos.json");
        return JsonSerializer.Deserialize<ProtoFileSnapshot[]>(File.ReadAllText(baselinePath))
            ?? throw new InvalidOperationException("The proto baseline is empty or invalid.");
    }
}
