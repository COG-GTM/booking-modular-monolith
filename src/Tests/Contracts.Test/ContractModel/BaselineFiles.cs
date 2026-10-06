namespace Contracts.Test.ContractModel;

using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core.Event;
using MassTransit;
using MassTransit.Serialization;

public static class BaselineFiles
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string OutputDirectory => Path.Combine(AppContext.BaseDirectory, "Baselines");

    public static T? Read<T>(string relativePath)
    {
        var path = Path.Combine(OutputDirectory, relativePath);
        return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) : default;
    }

    public static string SourcePath(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "booking-modular-monolith.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new DirectoryNotFoundException(
                "Could not find booking-modular-monolith.sln from the test output directory."
            );
        }

        return Path.Combine(directory.FullName, "src", "Tests", "Contracts.Test", "Baselines", relativePath);
    }

    public static void Write<T>(string relativePath, T value)
    {
        var sourcePath = SourcePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        File.WriteAllText(sourcePath, JsonSerializer.Serialize(value, Options) + Environment.NewLine);
    }

    public static string SerializeSample(Type eventType)
    {
        var constructor = eventType.GetConstructors().OrderByDescending(item => item.GetParameters().Length).First();
        var arguments = constructor
            .GetParameters()
            .Select(parameter => SampleValue(parameter.ParameterType, parameter.Name ?? "value"))
            .ToArray();
        var message = constructor.Invoke(arguments);
        return JsonSerializer.Serialize(message, SystemTextJsonMessageSerializer.Options);
    }

    public static IReadOnlyList<Type> ContractMessageTypes() =>
        typeof(UserCreated)
            .Assembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && typeof(IIntegrationEvent).IsAssignableFrom(type))
            .ToList();

    private static object? SampleValue(Type type, string name)
    {
        var underlyingType = Nullable.GetUnderlyingType(type);
        if (underlyingType is not null)
        {
            return SampleValue(underlyingType, name);
        }

        if (type == typeof(string))
        {
            return $"sample-{name}";
        }

        if (type == typeof(Guid))
        {
            return Guid.Empty;
        }

        if (type == typeof(DateTime))
        {
            return DateTime.UnixEpoch;
        }

        if (type == typeof(DateTimeOffset))
        {
            return DateTimeOffset.UnixEpoch;
        }

        if (type.IsEnum)
        {
            return Enum.GetValues(type).GetValue(0);
        }

        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}
