namespace Contracts.Compatibility.Test.Model;

using System.Text.Json;
using System.Text.Json.Serialization;

// Committed baselines live in tests/Contracts.Compatibility.Test/Baselines and are copied to the
// test output. Refresh them after an additive change with UPDATE_CONTRACT_BASELINES=true.
public static class Baselines
{
    public const string UpdateVariable = "UPDATE_CONTRACT_BASELINES";

    public const string MessagesFile = "messages/contracts.messages.json";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static bool UpdateRequested =>
        string.Equals(Environment.GetEnvironmentVariable(UpdateVariable), "true", StringComparison.OrdinalIgnoreCase);

    public static string OutputDirectory => Path.Combine(AppContext.BaseDirectory, "Baselines");

    public static string ProtoFile(string protoFileName) =>
        $"grpc/{Path.GetFileNameWithoutExtension(protoFileName)}.json";

    public static string SampleFile(Type messageType) => $"messages/samples/{messageType.Name}.json";

    public static IEnumerable<string> ExistingProtoBaselines()
    {
        var directory = Path.Combine(OutputDirectory, "grpc");
        return Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.json").Select(path => $"grpc/{Path.GetFileName(path)}")
            : [];
    }

    public static T? Read<T>(string relativePath)
        where T : class
    {
        var path = Path.Combine(OutputDirectory, relativePath);
        return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) : null;
    }

    public static string? ReadText(string relativePath)
    {
        var path = Path.Combine(OutputDirectory, relativePath);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options) + "\n";

    public static T Clone<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!;

    // Writes to the source tree (not bin/) so the change shows up in `git diff`.
    public static void Write(string relativePath, string content)
    {
        var path = Path.Combine(SourceDirectory(), relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string SourceDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "booking-modular-monolith.sln")))
        {
            directory = directory.Parent;
        }

        return directory is null
            ? throw new DirectoryNotFoundException("booking-modular-monolith.sln not found above the test output.")
            : Path.Combine(directory.FullName, "tests", "Contracts.Compatibility.Test", "Baselines");
    }
}
