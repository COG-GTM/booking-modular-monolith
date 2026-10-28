using System.Text.Json;
using Xunit;

namespace Contracts.Grpc.Test;

public sealed class ProtoCompatibilityCheckerTests
{
    private static ProtoFileSnapshot[] Baseline => ProtoBaselineTests.GetCurrentSnapshots();

    [Fact]
    public void Detects_field_tag_change()
    {
        var current = CloneBaseline();
        current = UpdateMessage(
            current,
            "flight.ReserveSeatRequest",
            message =>
                message with
                {
                    Fields = message
                        .Fields.Select(field => field.Name == "SeatNumber" ? field with { Number = 3 } : field)
                        .ToArray(),
                }
        );

        AssertViolation(current, "field number changed 2 -> 3");
    }

    [Fact]
    public void Detects_field_type_change()
    {
        var current = CloneBaseline();
        current = UpdateMessage(
            current,
            "flight.ReserveSeatRequest",
            message =>
                message with
                {
                    Fields = message
                        .Fields.Select(field => field.Name == "SeatNumber" ? field with { Type = "TYPE_INT32" } : field)
                        .ToArray(),
                }
        );

        AssertViolation(current, "field type changed");
    }

    [Fact]
    public void Detects_field_rename()
    {
        var current = CloneBaseline();
        current = UpdateMessage(
            current,
            "flight.ReserveSeatRequest",
            message =>
                message with
                {
                    Fields = message
                        .Fields.Select(field => field.Name == "SeatNumber" ? field with { Name = "Seat" } : field)
                        .ToArray(),
                }
        );

        AssertViolation(current, "field renamed to Seat");
    }

    [Fact]
    public void Detects_field_removal_without_reservation()
    {
        var current = CloneBaseline();
        current = UpdateMessage(
            current,
            "flight.ReserveSeatRequest",
            message => message with { Fields = message.Fields[..1] }
        );

        AssertViolation(current, "Field removed without reserving");
    }

    [Fact]
    public void Detects_rpc_removal()
    {
        var current = CloneBaseline();
        current = UpdateService(
            current,
            "flight.FlightGrpcService",
            service => service with { Methods = service.Methods[..^1] }
        );

        AssertViolation(current, "RPC removed or renamed");
    }

    [Fact]
    public void Detects_rpc_input_type_change()
    {
        var current = CloneBaseline();
        current = UpdateService(
            current,
            "flight.FlightGrpcService",
            service =>
                service with
                {
                    Methods = service
                        .Methods.Select(
                            (method, index) => index == 0 ? method with { InputType = ".flight.OtherRequest" } : method
                        )
                        .ToArray(),
                }
        );

        AssertViolation(current, "input type changed");
    }

    [Fact]
    public void Detects_package_rename()
    {
        var current = CloneBaseline();
        var index = Array.FindIndex(current, file => file.Package == "flight");
        current[index] = current[index] with { Package = "flight.v2" };

        AssertViolation(current, "Service removed or renamed: flight.FlightGrpcService");
    }

    [Fact]
    public void Allows_proto_file_rename()
    {
        var current = CloneBaseline().Select(file => file with { FileName = $"renamed/{file.FileName}" }).ToArray();

        Assert.Empty(ProtoCompatibility.Check(Baseline, current));
    }

    [Fact]
    public void Detects_enum_value_removal()
    {
        var current = CloneBaseline();
        current = UpdateEnum(current, "flight.FlightStatus", value => value with { Values = value.Values[..^1] });

        AssertViolation(current, "Enum value removed without reserving");
    }

    [Fact]
    public void Detects_enum_value_reusing_final_reserved_number()
    {
        var baseline = CloneBaseline();
        baseline = UpdateEnum(
            baseline,
            "flight.FlightStatus",
            value => value with { ReservedRanges = [.. value.ReservedRanges, new ProtoReservedRange(100, 200)] }
        );
        var current = CloneBaseline();
        current = UpdateEnum(
            current,
            "flight.FlightStatus",
            value => value with { Values = [.. value.Values, new ProtoEnumValueSnapshot("REUSED", 200)] }
        );

        Assert.Contains(
            ProtoCompatibility.Check(baseline, current),
            violation => violation.Contains("reuses a baseline-reserved number or name", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Allows_enum_value_removal_with_reserved_final_number()
    {
        var baseline = CloneBaseline();
        var lastValue = baseline
            .SelectMany(file => file.Enums)
            .Single(value => value.FullName == "flight.FlightStatus")
            .Values[^1];
        var current = CloneBaseline();
        current = UpdateEnum(
            current,
            "flight.FlightStatus",
            value =>
                value with
                {
                    Values = value.Values[..^1],
                    ReservedRanges =
                    [
                        .. value.ReservedRanges,
                        new ProtoReservedRange(lastValue.Number, lastValue.Number),
                    ],
                    ReservedNames = [.. value.ReservedNames, lastValue.Name],
                }
        );

        Assert.Empty(ProtoCompatibility.Check(baseline, current));
    }

    [Fact]
    public void Allows_field_removal_with_number_and_name_reserved()
    {
        var current = CloneBaseline();
        current = UpdateMessage(
            current,
            "flight.ReserveSeatRequest",
            message =>
                message with
                {
                    Fields = message.Fields[..1],
                    ReservedRanges = [.. message.ReservedRanges, new ProtoReservedRange(2, 3)],
                    ReservedNames = [.. message.ReservedNames, "SeatNumber"],
                }
        );

        Assert.Empty(ProtoCompatibility.Check(Baseline, current));
    }

    [Fact]
    public void Allows_new_field()
    {
        var current = CloneBaseline();
        current = UpdateMessage(
            current,
            "flight.ReserveSeatRequest",
            message =>
                message with
                {
                    Fields =
                    [
                        .. message.Fields,
                        new ProtoFieldSnapshot("SeatType", 3, "TYPE_STRING", "", "LABEL_OPTIONAL", false, null),
                    ],
                }
        );

        Assert.Empty(ProtoCompatibility.Check(Baseline, current));
    }

    [Fact]
    public void Allows_new_rpc()
    {
        var current = CloneBaseline();
        current = UpdateService(
            current,
            "flight.FlightGrpcService",
            service =>
                service with
                {
                    Methods =
                    [
                        .. service.Methods,
                        new ProtoMethodSnapshot(
                            "GetStatus",
                            ".flight.GetByIdRequest",
                            ".flight.GetFlightByIdResult",
                            false,
                            false
                        ),
                    ],
                }
        );

        Assert.Empty(ProtoCompatibility.Check(Baseline, current));
    }

    private static ProtoFileSnapshot[] CloneBaseline() =>
        JsonSerializer.Deserialize<ProtoFileSnapshot[]>(JsonSerializer.Serialize(Baseline))
        ?? throw new InvalidOperationException("Unable to clone proto snapshots.");

    private static ProtoFileSnapshot[] UpdateMessage(
        ProtoFileSnapshot[] snapshots,
        string name,
        Func<ProtoMessageSnapshot, ProtoMessageSnapshot> update
    )
    {
        var fileIndex = Array.FindIndex(snapshots, file => file.Messages.Any(message => message.FullName == name));
        var file = snapshots[fileIndex];
        snapshots[fileIndex] = file with
        {
            Messages = file.Messages.Select(message => message.FullName == name ? update(message) : message).ToArray(),
        };
        return snapshots;
    }

    private static ProtoFileSnapshot[] UpdateEnum(
        ProtoFileSnapshot[] snapshots,
        string name,
        Func<ProtoEnumSnapshot, ProtoEnumSnapshot> update
    )
    {
        var fileIndex = Array.FindIndex(snapshots, file => file.Enums.Any(value => value.FullName == name));
        var file = snapshots[fileIndex];
        snapshots[fileIndex] = file with
        {
            Enums = file.Enums.Select(value => value.FullName == name ? update(value) : value).ToArray(),
        };
        return snapshots;
    }

    private static ProtoFileSnapshot[] UpdateService(
        ProtoFileSnapshot[] snapshots,
        string fullName,
        Func<ProtoServiceSnapshot, ProtoServiceSnapshot> update
    )
    {
        var package = fullName[..fullName.LastIndexOf('.')];
        var name = fullName[(fullName.LastIndexOf('.') + 1)..];
        var fileIndex = Array.FindIndex(snapshots, file => file.Package == package);
        var file = snapshots[fileIndex];
        snapshots[fileIndex] = file with
        {
            Services = file.Services.Select(service => service.Name == name ? update(service) : service).ToArray(),
        };
        return snapshots;
    }

    private static void AssertViolation(ProtoFileSnapshot[] current, string expected) =>
        Assert.Contains(
            ProtoCompatibility.Check(Baseline, current),
            violation => violation.Contains(expected, StringComparison.Ordinal)
        );
}
