using System.Text.Json;
using FluentAssertions;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Timestamp = Google.Protobuf.WellKnownTypes.Timestamp;
using Xunit;

namespace Contracts.Grpc.Tests;

public class ProtoCompatibilityTests
{
    private static readonly FileDescriptor[] Descriptors =
    [
        Flight.FlightReflection.Descriptor,
        Passenger.PassengerReflection.Descriptor,
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    [Fact]
    public void grpc_contract_matches_the_committed_baseline()
    {
        var current = ProtoContractModel.FromDescriptors(Descriptors);
        var baselinePath = Path.Combine(
            RepositoryRoot(),
            "src",
            "Contracts",
            "tests",
            "Contracts.Grpc.Tests",
            "Baselines",
            "grpc-contract.json"
        );

        if (string.Equals(Environment.GetEnvironmentVariable("UPDATE_CONTRACT_BASELINES"), "true", StringComparison.OrdinalIgnoreCase))
        {
            File.WriteAllText(baselinePath, JsonSerializer.Serialize(current, JsonOptions) + Environment.NewLine);
            return;
        }

        var baseline = ReadBaseline();
        ProtoContractModel.BreakingChanges(baseline, current).Should().BeEmpty();
        JsonSerializer.Serialize(current, JsonOptions).Should().Be(JsonSerializer.Serialize(baseline, JsonOptions));
    }

    [Fact]
    public void compatibility_rules_detect_breaking_mutations_and_allow_additions()
    {
        var baseline = ReadBaseline();

        var withoutField = Clone(baseline);
        FlightResponse(withoutField).Fields.RemoveAll(field => field.Name == "Price");
        ProtoContractModel.BreakingChanges(baseline, withoutField)
            .Should()
            .ContainSingle(change => change.Contains("FlightResponse.Price", StringComparison.Ordinal));

        var changedPackage = Clone(baseline);
        var packageFileIndex = changedPackage.Files.FindIndex(file => file.Name == "flight.proto");
        changedPackage.Files[packageFileIndex] = changedPackage.Files[packageFileIndex] with { Package = "flight.v2" };
        ProtoContractModel.BreakingChanges(baseline, changedPackage)
            .Should()
            .Contain(change => change.Contains("package changed", StringComparison.Ordinal));

        var removedFile = Clone(baseline);
        removedFile.Files.RemoveAll(file => file.Name == "flight.proto");
        ProtoContractModel.BreakingChanges(baseline, removedFile)
            .Should()
            .Contain(change => change.Contains("flight.proto: file removed", StringComparison.Ordinal));

        var removedService = Clone(baseline);
        removedService.Files.Single(file => file.Name == "flight.proto").Services.Clear();
        ProtoContractModel.BreakingChanges(baseline, removedService)
            .Should()
            .Contain(change => change.Contains("service removed", StringComparison.Ordinal));

        var removedMessage = Clone(baseline);
        removedMessage.Files.Single(file => file.Name == "flight.proto").Messages.RemoveAll(message =>
            message.FullName == "flight.GetByIdRequest"
        );
        ProtoContractModel.BreakingChanges(baseline, removedMessage)
            .Should()
            .Contain(change => change.Contains("flight.GetByIdRequest: message removed", StringComparison.Ordinal));

        var removedEnum = Clone(baseline);
        removedEnum.Files.Single(file => file.Name == "flight.proto").Enums.RemoveAll(enumType =>
            enumType.FullName == "flight.FlightStatus"
        );
        ProtoContractModel.BreakingChanges(baseline, removedEnum)
            .Should()
            .Contain(change => change.Contains("flight.FlightStatus: enum removed", StringComparison.Ordinal));

        var retypedField = Clone(baseline);
        ReplaceField(
            FlightResponse(retypedField),
            field => field.Name == "Price",
            field => field with { Type = "string" }
        );
        ProtoContractModel.BreakingChanges(baseline, retypedField)
            .Should()
            .Contain(change => change.Contains("field number 11", StringComparison.Ordinal));

        var renumberedField = Clone(baseline);
        ReplaceField(
            FlightResponse(renumberedField),
            field => field.Name == "Price",
            field => field with { Number = 13 }
        );
        ProtoContractModel.BreakingChanges(baseline, renumberedField)
            .Should()
            .Contain(change => change.Contains("FlightResponse.Price: field moved", StringComparison.Ordinal));

        var withoutMethod = Clone(baseline);
        FlightService(withoutMethod).Methods.RemoveAll(method => method.Name == "GetById");
        ProtoContractModel.BreakingChanges(baseline, withoutMethod)
            .Should()
            .Contain(change => change.Contains("GetById: method removed", StringComparison.Ordinal));

        var changedInput = Clone(baseline);
        var getByIdIndex = FlightService(changedInput).Methods.FindIndex(method => method.Name == "GetById");
        FlightService(changedInput).Methods[getByIdIndex] = FlightService(changedInput).Methods[getByIdIndex] with
        {
            InputType = "flight.ReserveSeatRequest",
        };
        ProtoContractModel.BreakingChanges(baseline, changedInput)
            .Should()
            .Contain(change => change.Contains("input type changed", StringComparison.Ordinal));

        var changedOutput = Clone(baseline);
        var outputMethodIndex = FlightService(changedOutput).Methods.FindIndex(method => method.Name == "GetById");
        FlightService(changedOutput).Methods[outputMethodIndex] =
            FlightService(changedOutput).Methods[outputMethodIndex] with { OutputType = "flight.ReserveSeatResult" };
        ProtoContractModel.BreakingChanges(baseline, changedOutput)
            .Should()
            .Contain(change => change.Contains("output type changed", StringComparison.Ordinal));

        var changedStreaming = Clone(baseline);
        var streamingMethodIndex = FlightService(changedStreaming).Methods.FindIndex(method => method.Name == "GetById");
        FlightService(changedStreaming).Methods[streamingMethodIndex] =
            FlightService(changedStreaming).Methods[streamingMethodIndex] with { ServerStreaming = true };
        ProtoContractModel.BreakingChanges(baseline, changedStreaming)
            .Should()
            .Contain(change => change.Contains("streaming changed", StringComparison.Ordinal));

        var withoutEnumValue = Clone(baseline);
        FlightStatus(withoutEnumValue).Values.RemoveAll(value => value.Name == "FLIGHT_STATUS_COMPLETED");
        ProtoContractModel.BreakingChanges(baseline, withoutEnumValue)
            .Should()
            .Contain(change => change.Contains("FLIGHT_STATUS_COMPLETED", StringComparison.Ordinal));

        var renumberedEnumValue = Clone(baseline);
        var completedIndex = FlightStatus(renumberedEnumValue).Values.FindIndex(value =>
            value.Name == "FLIGHT_STATUS_COMPLETED"
        );
        FlightStatus(renumberedEnumValue).Values[completedIndex] =
            FlightStatus(renumberedEnumValue).Values[completedIndex] with { Number = 99 };
        ProtoContractModel.BreakingChanges(baseline, renumberedEnumValue)
            .Should()
            .Contain(change => change.Contains("enum value renumbered", StringComparison.Ordinal));

        var reusedFieldNumber = Clone(baseline);
        ReplaceField(
            FlightResponse(reusedFieldNumber),
            field => field.Name == "Price",
            field => field with { Name = "OtherField", JsonName = "otherField" }
        );
        ProtoContractModel.BreakingChanges(baseline, reusedFieldNumber)
            .Should()
            .Contain(change => change.Contains("field number 11 now used", StringComparison.Ordinal));

        var reservedBaseline = Clone(baseline);
        FlightResponse(reservedBaseline).ReservedNumbers.Add(new ProtoNumberRange(13, 14));
        var reusedReservedNumber = Clone(reservedBaseline);
        FlightResponse(reusedReservedNumber).Fields.Add(new ProtoFieldContract(13, "NewField", "string", "optional", "newField"));
        ProtoContractModel.BreakingChanges(reservedBaseline, reusedReservedNumber)
            .Should()
            .Contain(change => change.Contains("reserved field number 13 reused", StringComparison.Ordinal));

        var enumReservedBaseline = Clone(baseline);
        FlightStatus(enumReservedBaseline).ReservedNames.Add("FLIGHT_STATUS_OLD");
        var reusedEnumReservedName = Clone(enumReservedBaseline);
        FlightStatus(reusedEnumReservedName).Values.Add(new ProtoEnumValueContract(99, "FLIGHT_STATUS_OLD"));
        ProtoContractModel.BreakingChanges(enumReservedBaseline, reusedEnumReservedName)
            .Should()
            .Contain(change => change.Contains("reserved enum name FLIGHT_STATUS_OLD reused", StringComparison.Ordinal));

        var additions = Clone(baseline);
        FlightResponse(additions).Fields.Add(new ProtoFieldContract(13, "NewField", "string", "optional", "newField"));
        FlightService(additions).Methods.Add(
            new ProtoMethodContract("NewMethod", "flight.GetByIdRequest", "flight.ReserveSeatResult", false, false)
        );
        additions.Files.Single(file => file.Name == "flight.proto").Messages.Add(
            new ProtoMessageContract("flight.NewMessage", [], [], [])
        );
        FlightStatus(additions).Values.Add(new ProtoEnumValueContract(99, "FLIGHT_STATUS_NEW"));
        ProtoContractModel.BreakingChanges(baseline, additions).Should().BeEmpty();
    }

    [Fact]
    public void extraction_and_diff_work_with_a_real_mutated_file_descriptor()
    {
        var mutatedProto = Flight.FlightReflection.Descriptor.ToProto();
        var response = mutatedProto.MessageType.Single(message => message.Name == "FlightResponse");
        var price = response.Field.Single(field => field.Name == "Price");
        response.Field.Remove(price);

        var mutatedFlight = FileDescriptor
            .BuildFromByteStrings(
                [
                    Timestamp.Descriptor.File.ToProto().ToByteString(),
                    mutatedProto.ToByteString(),
                ]
            )
            .Single(file => file.Name == mutatedProto.Name);

        var current = ProtoContractModel.FromDescriptors(mutatedFlight, Passenger.PassengerReflection.Descriptor);
        var changes = ProtoContractModel.BreakingChanges(ReadBaseline(), current);

        changes.Should().Contain(change => change.Contains("FlightResponse.Price", StringComparison.Ordinal));
    }

    private static ProtoContractModel ReadBaseline()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Baselines", "grpc-contract.json");
        return JsonSerializer.Deserialize<ProtoContractModel>(File.ReadAllText(path), JsonOptions)!;
    }

    private static ProtoContractModel Clone(ProtoContractModel model) =>
        JsonSerializer.Deserialize<ProtoContractModel>(JsonSerializer.Serialize(model, JsonOptions), JsonOptions)!;

    private static ProtoMessageContract FlightResponse(ProtoContractModel model) =>
        model.Files.SelectMany(file => file.Messages).Single(message => message.FullName == "flight.FlightResponse");

    private static ProtoServiceContract FlightService(ProtoContractModel model) =>
        model.Files.SelectMany(file => file.Services).Single(service => service.FullName == "flight.FlightGrpcService");

    private static ProtoEnumContract FlightStatus(ProtoContractModel model) =>
        model.Files.SelectMany(file => file.Enums).Single(enumType => enumType.FullName == "flight.FlightStatus");

    private static void ReplaceField(
        ProtoMessageContract message,
        Predicate<ProtoFieldContract> predicate,
        Func<ProtoFieldContract, ProtoFieldContract> replacement
    )
    {
        var index = message.Fields.FindIndex(predicate);
        message.Fields[index] = replacement(message.Fields[index]);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "booking-modular-monolith.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
