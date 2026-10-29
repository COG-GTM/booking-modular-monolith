using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using MassTransit;
using MassTransit.Serialization;
using Xunit;
using Timestamp = Google.Protobuf.WellKnownTypes.Timestamp;

namespace Contracts.ConsumerDriven.Tests;

public class ConsumerDrivenContractTests
{
    private static readonly JsonSerializerOptions PactJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly FileDescriptor[] ProviderDescriptors =
    [
        Flight.FlightReflection.Descriptor,
        Passenger.PassengerReflection.Descriptor,
    ];

    [Fact]
    public void consumer_pacts_match_the_current_provider_contracts()
    {
        var errors = new List<string>();
        foreach (var pact in LoadMessagePacts())
            errors.AddRange(VerifyMessagePact(pact, ContractsMessagesTypes()));

        foreach (var pact in LoadGrpcPacts())
            errors.AddRange(VerifyGrpcPact(pact, ProviderDescriptors));

        errors.Should().BeEmpty(string.Join(Environment.NewLine, errors));
    }

    [Fact]
    public void grpc_pact_samples_round_trip_through_provider_parsers()
    {
        var failures = new List<string>();
        foreach (var pact in LoadGrpcPacts())
        {
            foreach (var interaction in pact.Interactions)
            {
                RoundTrip(interaction.Request, pact, ProviderDescriptors, failures);
                RoundTrip(interaction.Response, pact, ProviderDescriptors, failures);
            }
        }

        failures.Should().BeEmpty(string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void every_documented_consumer_has_a_pact_for_an_existing_provider()
    {
        var messagePacts = LoadMessagePacts();
        var grpcPacts = LoadGrpcPacts();
        var readme = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Contracts", "README.md"));

        readme.Should().Contain("UserCreated").And.Contain("Passenger");
        messagePacts.Should().ContainSingle(pact => pact.MessageUrn == "urn:message:BuildingBlocks.Contracts.EventBus.Messages:UserCreated");
        grpcPacts.Should().ContainSingle(pact => pact.Consumer == "Booking" && pact.Provider == "Flight");
        grpcPacts.Should().ContainSingle(pact => pact.Consumer == "Booking" && pact.Provider == "Passenger");
        messagePacts.SelectMany(pact => VerifyMessagePact(pact, ContractsMessagesTypes()))
            .Should()
            .BeEmpty();
        grpcPacts.SelectMany(pact => VerifyGrpcPact(pact, ProviderDescriptors)).Should().BeEmpty();
    }

    [Fact]
    public void message_verifier_reports_missing_and_retyped_provider_fields()
    {
        var pact = LoadMessagePacts().Single();

        VerifyMessageType(pact, typeof(MissingPassportNumber))
            .Should()
            .Contain(error => error.Contains("PassportNumber", StringComparison.Ordinal));
        VerifyMessageType(pact, typeof(RetypedPassportNumber))
            .Should()
            .Contain(error => error.Contains("PassportNumber", StringComparison.Ordinal));
    }

    [Fact]
    public void grpc_verifier_reports_removed_and_renumbered_provider_fields()
    {
        var pact = LoadGrpcPacts().Single(pact => pact.Provider == "Flight");

        VerifyGrpcPact(pact, [MutatedFlightDescriptor(removePrice: true)])
            .Should()
            .Contain(error => error.Contains("Price", StringComparison.Ordinal));
        VerifyGrpcPact(pact, [MutatedFlightDescriptor(priceNumber: 14)])
            .Should()
            .Contain(error => error.Contains("Price", StringComparison.Ordinal));
    }

    private static List<string> VerifyMessagePact(MessagePact pact, IEnumerable<Type> providerTypes)
    {
        var providerType = providerTypes.SingleOrDefault(type =>
            string.Equals(MessageUrn.ForTypeString(type), pact.MessageUrn, StringComparison.Ordinal)
        );
        return providerType is null
            ? [$"{pact.MessageUrn}: provider message type not found"]
            : VerifyMessageType(pact, providerType);
    }

    private static List<string> VerifyMessageType(MessagePact pact, Type providerType)
    {
        var failures = new List<string>();
        foreach (var required in pact.RequiredFields)
        {
            var property = providerType.GetProperty(
                required.Property,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase
            );
            if (property is null)
            {
                failures.Add($"{providerType.Name}: missing required field {required.Property}");
                continue;
            }

            var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (!IsCompatibleMessageType(propertyType, required.JsonKind))
                failures.Add($"{providerType.Name}.{required.Property}: incompatible field type {propertyType.Name}");
        }

        if (failures.Count != 0)
            return failures;

        try
        {
            var sample = JsonSerializer.Deserialize(pact.Sample.GetRawText(), providerType, SystemTextJsonMessageSerializer.Options);
            if (sample is null)
            {
                failures.Add($"{providerType.Name}: sample payload deserialized to null");
                return failures;
            }

            using var serialized = JsonDocument.Parse(
                JsonSerializer.Serialize(sample, providerType, SystemTextJsonMessageSerializer.Options)
            );
            foreach (var required in pact.RequiredFields)
            {
                if (!serialized.RootElement.TryGetProperty(required.JsonName, out var value))
                {
                    failures.Add($"{providerType.Name}: serialized sample is missing {required.JsonName}");
                    continue;
                }

                if (!Enum.TryParse<JsonValueKind>(required.JsonKind, ignoreCase: true, out var expectedKind) || value.ValueKind != expectedKind)
                    failures.Add($"{providerType.Name}.{required.JsonName}: expected JSON {required.JsonKind}, found {value.ValueKind}");
                if (!pact.Sample.TryGetProperty(required.JsonName, out var originalValue))
                    failures.Add($"{providerType.Name}: pact sample is missing {required.JsonName}");
                else if (originalValue.ToString() != value.ToString())
                    failures.Add($"{providerType.Name}.{required.JsonName}: sample value changed during serialization");
            }

            var roundTrip = JsonSerializer.Deserialize(
                serialized.RootElement.GetRawText(),
                providerType,
                SystemTextJsonMessageSerializer.Options
            );
            if (roundTrip is null)
                failures.Add($"{providerType.Name}: serialized sample could not be read back");
        }
        catch (Exception exception)
        {
            failures.Add($"{providerType.Name}: sample payload failed serialization: {exception.Message}");
        }

        return failures;
    }

    private static List<string> VerifyGrpcPact(GrpcPact pact, IEnumerable<FileDescriptor> providerFiles)
    {
        var failures = new List<string>();
        var services = providerFiles.SelectMany(file => file.Services);
        var service = services.SingleOrDefault(candidate => candidate.FullName == pact.Service);
        if (service is null)
            return [$"{pact.Service}: provider service not found"];

        foreach (var interaction in pact.Interactions)
        {
            var method = service.Methods.SingleOrDefault(candidate => candidate.Name == interaction.Method);
            if (method is null)
            {
                failures.Add($"{pact.Service}/{interaction.Method}: provider method not found");
                continue;
            }

            if (method.InputType.FullName != interaction.Request.Type)
                failures.Add($"{pact.Service}/{interaction.Method}: request type {method.InputType.FullName} does not match {interaction.Request.Type}");
            if (method.OutputType.FullName != interaction.Response.Type)
                failures.Add($"{pact.Service}/{interaction.Method}: response type {method.OutputType.FullName} does not match {interaction.Response.Type}");

            VerifyGrpcMessage(interaction.Request, providerFiles, failures);
            VerifyGrpcMessage(interaction.Response, providerFiles, failures);
        }

        return failures;
    }

    private static void VerifyGrpcMessage(
        GrpcMessagePact pact,
        IEnumerable<FileDescriptor> providerFiles,
        ICollection<string> failures
    )
    {
        var descriptor = providerFiles
            .SelectMany(file => FlattenMessages(file.MessageTypes))
            .SingleOrDefault(message => message.FullName == pact.Type);
        if (descriptor is null)
        {
            failures.Add($"{pact.Type}: provider message not found");
            return;
        }

        foreach (var expected in pact.Fields)
        {
            var current = descriptor;
            FieldDescriptor? field = null;
            foreach (var segment in expected.Path.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                field = current.FindFieldByName(segment);
                if (field is null)
                    break;
                if (segment != expected.Path.Split('.').Last())
                {
                    if (field.FieldType != FieldType.Message)
                    {
                        field = null;
                        break;
                    }

                    current = field.MessageType;
                }
            }

            if (field is null)
            {
                failures.Add($"{pact.Type}.{expected.Path}: provider field not found");
                continue;
            }

            if (field.FieldNumber != expected.Number)
                failures.Add($"{pact.Type}.{expected.Path}: field number {field.FieldNumber} does not match {expected.Number}");
            if (field.Name != expected.Name)
                failures.Add($"{pact.Type}.{expected.Path}: field name {field.Name} does not match {expected.Name}");
            if (FieldTypeName(field) != expected.Type)
                failures.Add($"{pact.Type}.{expected.Path}: field type {FieldTypeName(field)} does not match {expected.Type}");
            if (FieldLabel(field) != expected.Label)
                failures.Add($"{pact.Type}.{expected.Path}: field label {FieldLabel(field)} does not match {expected.Label}");
        }
    }

    private static void RoundTrip(
        GrpcMessagePact pact,
        GrpcPact grpcPact,
        IEnumerable<FileDescriptor> providerFiles,
        ICollection<string> failures
    )
    {
        try
        {
            var descriptor = providerFiles
                .SelectMany(file => FlattenMessages(file.MessageTypes))
                .Single(message => message.FullName == pact.Type);
            var parsed = JsonParser.Default.Parse(pact.Sample.GetRawText(), descriptor);
            var bytes = parsed.ToByteArray();
            var roundTrip = descriptor.Parser.ParseFrom(bytes);
            if (!roundTrip.ToByteArray().SequenceEqual(bytes))
                failures.Add($"{grpcPact.Service}/{pact.Type}: sample wire round-trip changed the payload");
        }
        catch (Exception exception)
        {
            failures.Add($"{grpcPact.Service}/{pact.Type}: sample failed wire round-trip: {exception.Message}");
        }
    }

    private static FileDescriptor MutatedFlightDescriptor(bool removePrice = false, int? priceNumber = null)
    {
        var proto = Flight.FlightReflection.Descriptor.ToProto();
        var response = proto.MessageType.Single(message => message.Name == "FlightResponse");
        var price = response.Field.Single(field => field.Name == "Price");
        if (removePrice)
            response.Field.Remove(price);
        else if (priceNumber.HasValue)
            price.Number = priceNumber.Value;

        return FileDescriptor
            .BuildFromByteStrings(
                [
                    Timestamp.Descriptor.File.ToProto().ToByteString(),
                    proto.ToByteString(),
                ]
            )
            .Single(file => file.Name == proto.Name);
    }

    private static IEnumerable<Type> ContractsMessagesTypes() =>
        typeof(BuildingBlocks.Contracts.EventBus.Messages.UserCreated).Assembly.GetTypes();

    private static bool IsCompatibleMessageType(Type type, string jsonKind) =>
        jsonKind switch
        {
            "String" => type == typeof(string) || type == typeof(Guid),
            "Number" => type.IsPrimitive && type != typeof(bool) && type != typeof(char) || type == typeof(decimal),
            "True" or "False" => type == typeof(bool),
            "Object" => !type.IsPrimitive && type != typeof(string),
            "Array" => typeof(System.Collections.IEnumerable).IsAssignableFrom(type) && type != typeof(string),
            _ => false,
        };

    private static string FieldTypeName(FieldDescriptor field) =>
        field.FieldType switch
        {
            FieldType.Message => field.MessageType.FullName,
            FieldType.Enum => field.EnumType.FullName,
            _ => field.FieldType.ToString().ToLowerInvariant(),
        };

    private static string FieldLabel(FieldDescriptor field) =>
        field.IsRepeated ? "repeated" : field.IsRequired ? "required" : "optional";

    private static IEnumerable<MessageDescriptor> FlattenMessages(IEnumerable<MessageDescriptor> messages)
    {
        foreach (var message in messages)
        {
            yield return message;
            foreach (var nested in FlattenMessages(message.NestedTypes))
                yield return nested;
        }
    }

    private static List<MessagePact> LoadMessagePacts() =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "pacts"), "*.messages.json")
            .Select(path => JsonSerializer.Deserialize<MessagePact>(File.ReadAllText(path), PactJsonOptions)!)
            .ToList();

    private static List<GrpcPact> LoadGrpcPacts() =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "pacts"), "*.grpc.json")
            .Select(path => JsonSerializer.Deserialize<GrpcPact>(File.ReadAllText(path), PactJsonOptions)!)
            .ToList();

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "booking-modular-monolith.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed record MessagePact(
        string Consumer,
        string ConsumerImplementation,
        string Provider,
        string Queue,
        string MessageUrn,
        List<MessagePactField> RequiredFields,
        JsonElement Sample
    );

    private sealed record MessagePactField(string Property, string JsonName, string JsonKind);

    private sealed record GrpcPact(string Consumer, string ConsumerImplementation, string Provider, string Service, List<GrpcInteraction> Interactions);

    private sealed record GrpcInteraction(
        string Method,
        GrpcMessagePact Request,
        GrpcMessagePact Response
    );

    private sealed record GrpcMessagePact(string Type, List<GrpcFieldPact> Fields, JsonElement Sample);

    private sealed record GrpcFieldPact(string Path, int Number, string Name, string Type, string Label);

    private sealed record MissingPassportNumber(Guid Id, string Name);

    private sealed record RetypedPassportNumber(Guid Id, string Name, int PassportNumber);
}
