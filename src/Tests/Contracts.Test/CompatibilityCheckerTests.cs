namespace Contracts.Test;

using Contracts.Test.ContractModel;
using FluentAssertions;
using Xunit;

public class CompatibilityCheckerTests
{
    [Fact]
    public void grpc_package_change_is_breaking()
    {
        CompareGrpc(current => current.Package = "flight.v2")
            .Should()
            .ContainSingle(change => change.Contains("package changed"));
    }

    [Fact]
    public void grpc_file_change_is_breaking()
    {
        CompareGrpc(current => current.FileName = "renamed.proto")
            .Should()
            .ContainSingle(change => change.Contains("file removed or renamed"));
    }

    [Fact]
    public void removed_service_is_breaking()
    {
        CompareGrpc(current => current.Services.Clear())
            .Should()
            .ContainSingle(change => change.Contains("service removed"));
    }

    [Fact]
    public void removed_method_is_breaking()
    {
        CompareGrpc(current => current.Services["FlightGrpcService"].Methods.Clear())
            .Should()
            .ContainSingle(change => change.Contains("method removed"));
    }

    [Fact]
    public void changed_method_input_type_is_breaking()
    {
        CompareGrpc(current => current.Services["FlightGrpcService"].Methods["GetById"].InputType = ".flight.v1.Other")
            .Should()
            .ContainSingle(change => change.Contains("input type changed"));
    }

    [Fact]
    public void changed_method_output_type_is_breaking()
    {
        CompareGrpc(current => current.Services["FlightGrpcService"].Methods["GetById"].OutputType = ".flight.v1.Other")
            .Should()
            .ContainSingle(change => change.Contains("output type changed"));
    }

    [Fact]
    public void changed_method_streaming_is_breaking()
    {
        CompareGrpc(current => current.Services["FlightGrpcService"].Methods["GetById"].ServerStreaming = true)
            .Should()
            .ContainSingle(change => change.Contains("streaming changed"));
    }

    [Fact]
    public void removed_message_is_breaking()
    {
        CompareGrpc(current => current.Messages.Clear())
            .Should()
            .ContainSingle(change => change.Contains("message removed"));
    }

    [Fact]
    public void removed_field_without_both_reservations_is_breaking()
    {
        CompareGrpc(current => current.Messages[".flight.v1.GetByIdRequest"].Fields.Clear())
            .Should()
            .ContainSingle(change => change.Contains("field removed without reserving"));
    }

    [Fact]
    public void removed_field_with_number_and_name_reserved_is_compatible()
    {
        var changes = CompareGrpc(current =>
        {
            var message = current.Messages[".flight.v1.GetByIdRequest"];
            message.Fields.Clear();
            message.ReservedNames.Add("Id");
            message.ReservedRanges.Add(new ProtoReservedRange(1, 2));
        });

        changes.Should().NotContain(change => change.Contains("field removed"));
    }

    [Fact]
    public void field_rename_on_same_number_is_breaking()
    {
        CompareGrpc(current => current.Messages[".flight.v1.GetByIdRequest"].Fields[1].Name = "NewId")
            .Should()
            .ContainSingle(change => change.Contains("field renamed"));
    }

    [Fact]
    public void field_number_change_is_breaking()
    {
        CompareGrpc(current =>
            {
                var field = current.Messages[".flight.v1.GetByIdRequest"].Fields[1];
                current.Messages[".flight.v1.GetByIdRequest"].Fields.Remove(1);
                field.Number = 2;
                current.Messages[".flight.v1.GetByIdRequest"].Fields.Add(2, field);
            })
            .Should()
            .ContainSingle(change => change.Contains("field removed without reserving"));
    }

    [Fact]
    public void field_type_change_is_breaking()
    {
        CompareGrpc(current => current.Messages[".flight.v1.GetByIdRequest"].Fields[1].Type = "TYPE_BYTES")
            .Should()
            .ContainSingle(change => change.Contains("field type changed"));
    }

    [Fact]
    public void field_type_name_change_is_breaking()
    {
        CompareGrpc(current => current.Messages[".flight.v1.GetByIdRequest"].Fields[1].TypeName = ".other.Type")
            .Should()
            .ContainSingle(change => change.Contains("field type_name changed"));
    }

    [Fact]
    public void field_label_change_is_breaking()
    {
        CompareGrpc(current => current.Messages[".flight.v1.GetByIdRequest"].Fields[1].Label = "LABEL_REPEATED")
            .Should()
            .ContainSingle(change => change.Contains("field label changed"));
    }

    [Fact]
    public void field_json_name_change_is_breaking()
    {
        CompareGrpc(current => current.Messages[".flight.v1.GetByIdRequest"].Fields[1].JsonName = "newId")
            .Should()
            .ContainSingle(change => change.Contains("field json_name changed"));
    }

    [Fact]
    public void removed_enum_is_breaking()
    {
        CompareGrpc(current => current.Enums.Clear()).Should().ContainSingle(change => change.Contains("enum removed"));
    }

    [Fact]
    public void removed_enum_value_without_reservation_is_breaking()
    {
        CompareGrpc(current => current.Enums[".flight.v1.FlightStatus"].Values.Remove("FLIGHT_STATUS_FLYING"))
            .Should()
            .ContainSingle(change => change.Contains("enum value removed"));
    }

    [Fact]
    public void removed_enum_value_reserved_by_name_is_compatible()
    {
        var changes = CompareGrpc(current =>
        {
            var status = current.Enums[".flight.v1.FlightStatus"];
            status.Values.Remove("FLIGHT_STATUS_FLYING");
            status.ReservedNames.Add("FLIGHT_STATUS_FLYING");
        });

        changes.Should().NotContain(change => change.Contains("enum value removed"));
    }

    [Fact]
    public void removed_enum_value_reserved_by_number_is_compatible()
    {
        var changes = CompareGrpc(current =>
        {
            var status = current.Enums[".flight.v1.FlightStatus"];
            status.Values.Remove("FLIGHT_STATUS_FLYING");
            status.ReservedRanges.Add(new ProtoReservedRange(1, 2));
        });

        changes.Should().NotContain(change => change.Contains("enum value removed"));
    }

    [Fact]
    public void renumbered_enum_value_is_breaking()
    {
        CompareGrpc(current => current.Enums[".flight.v1.FlightStatus"].Values["FLIGHT_STATUS_FLYING"] = 8)
            .Should()
            .ContainSingle(change => change.Contains("enum value renumbered"));
    }

    [Fact]
    public void removed_message_type_is_breaking()
    {
        CompatibilityChecker
            .Compare(new MessagesSnapshot { Messages = { ["Test.Event"] = Message() } }, new MessagesSnapshot())
            .Should()
            .ContainSingle(change => change.Contains("Message type removed"));
    }

    [Fact]
    public void changed_message_urn_is_breaking()
    {
        var baseline = new MessagesSnapshot { Messages = { ["Test.Event"] = Message() } };
        var current = new MessagesSnapshot { Messages = { ["Test.Event"] = Message("urn:message:Test:Renamed") } };

        CompatibilityChecker
            .Compare(baseline, current)
            .Should()
            .ContainSingle(change => change.Contains("URN changed"));
    }

    [Fact]
    public void removed_message_property_is_breaking()
    {
        var baseline = new MessagesSnapshot { Messages = { ["Test.Event"] = Message() } };
        var currentMessage = Message();
        currentMessage.Properties.Clear();
        var current = new MessagesSnapshot { Messages = { ["Test.Event"] = currentMessage } };

        CompatibilityChecker
            .Compare(baseline, current)
            .Should()
            .ContainSingle(change => change.Contains("property removed"));
    }

    [Fact]
    public void changed_message_property_type_is_breaking()
    {
        var baseline = new MessagesSnapshot { Messages = { ["Test.Event"] = Message() } };
        var currentMessage = Message();
        currentMessage.Properties["Id"].Type = "System.String";
        var current = new MessagesSnapshot { Messages = { ["Test.Event"] = currentMessage } };

        CompatibilityChecker
            .Compare(baseline, current)
            .Should()
            .ContainSingle(change => change.Contains("property type changed"));
    }

    [Fact]
    public void changed_message_property_nullability_is_breaking()
    {
        var baseline = new MessagesSnapshot { Messages = { ["Test.Event"] = Message() } };
        var currentMessage = Message();
        currentMessage.Properties["Id"].Nullability = "Nullable";
        var current = new MessagesSnapshot { Messages = { ["Test.Event"] = currentMessage } };

        CompatibilityChecker
            .Compare(baseline, current)
            .Should()
            .ContainSingle(change => change.Contains("property nullability changed"));
    }

    [Fact]
    public void added_fields_methods_enum_values_and_properties_are_compatible()
    {
        var grpcBaseline = Grpc();
        var grpcCurrent = Grpc();
        grpcCurrent.Messages[".flight.v1.GetByIdRequest"].Fields[2] = new ProtoFieldSnapshot
        {
            Name = "Extra",
            Number = 2,
            Type = "TYPE_STRING",
            Label = "LABEL_OPTIONAL",
            JsonName = "extra",
        };
        grpcCurrent.Services["FlightGrpcService"].Methods["Added"] = new GrpcMethodSnapshot();
        grpcCurrent.Enums[".flight.v1.FlightStatus"].Values["FLIGHT_STATUS_NEW"] = 10;
        CompatibilityChecker.Compare(grpcBaseline, grpcCurrent).Should().BeEmpty();

        var messageBaseline = new MessagesSnapshot { Messages = { ["Test.Event"] = Message() } };
        var messageCurrent = new MessagesSnapshot { Messages = { ["Test.Event"] = Message() } };
        messageCurrent.Messages["Added.Event"] = Message();
        messageCurrent.Messages["Test.Event"].Properties["Name"] = new MessagePropertySnapshot
        {
            Type = "System.String",
        };
        CompatibilityChecker.Compare(messageBaseline, messageCurrent).Should().BeEmpty();
    }

    [Fact]
    public void reserved_removed_field_still_breaks_a_consumer_pact_that_uses_it()
    {
        var current = Grpc();
        var message = current.Messages[".flight.v1.GetByIdRequest"];
        message.Fields.Clear();
        message.ReservedNames.Add("Id");
        message.ReservedRanges.Add(new ProtoReservedRange(1, 2));
        var pact = new GrpcPact
        {
            Package = "flight.v1",
            Messages =
            [
                new GrpcPactMessage
                {
                    Name = "GetByIdRequest",
                    Fields =
                    [
                        new GrpcPactField
                        {
                            Name = "Id",
                            Number = 1,
                            Type = "TYPE_STRING",
                            Label = "LABEL_OPTIONAL",
                        },
                    ],
                },
            ],
        };

        CompatibilityChecker.Compare(Grpc(), current).Should().BeEmpty();
        PactContracts.Validate(pact, current).Should().ContainSingle(change => change.Contains("field pact mismatch"));
    }

    private static IReadOnlyList<string> CompareGrpc(Action<GrpcSnapshot> mutateCurrent)
    {
        var baseline = Grpc();
        var current = Grpc();
        mutateCurrent(current);
        return CompatibilityChecker.Compare(baseline, current);
    }

    private static GrpcSnapshot Grpc() =>
        new()
        {
            Package = "flight.v1",
            FileName = "flight.proto",
            Services =
            {
                ["FlightGrpcService"] = new GrpcServiceSnapshot
                {
                    Methods =
                    {
                        ["GetById"] = new GrpcMethodSnapshot
                        {
                            InputType = ".flight.v1.GetByIdRequest",
                            OutputType = ".flight.v1.GetFlightByIdResult",
                        },
                    },
                },
            },
            Messages =
            {
                [".flight.v1.GetByIdRequest"] = new ProtoMessageSnapshot
                {
                    Fields =
                    {
                        [1] = new ProtoFieldSnapshot
                        {
                            Name = "Id",
                            Number = 1,
                            Type = "TYPE_STRING",
                            Label = "LABEL_OPTIONAL",
                            JsonName = "id",
                        },
                    },
                },
            },
            Enums =
            {
                [".flight.v1.FlightStatus"] = new ProtoEnumSnapshot
                {
                    Values = { ["FLIGHT_STATUS_UNKNOWN"] = 0, ["FLIGHT_STATUS_FLYING"] = 1 },
                },
            },
        };

    private static MessageSnapshot Message(string urn = "urn:message:Test:Event") =>
        new()
        {
            Urn = urn,
            Properties =
            {
                ["Id"] = new MessagePropertySnapshot { Type = "System.Guid", Nullability = "NotNull" },
            },
        };
}
