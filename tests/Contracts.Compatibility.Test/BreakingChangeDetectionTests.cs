namespace Contracts.Compatibility.Test;

using Contracts.Compatibility.Test.Model;
using FluentAssertions;
using Google.Protobuf.Reflection;
using Xunit;

// Negative runs: each case applies a known-incompatible edit to the real compiled contracts and
// asserts the checks that guard CI reject it. Additive edits must still pass.
public class BreakingChangeDetectionTests
{
    private const string UserCreatedName = "BuildingBlocks.Contracts.EventBus.Messages.UserCreated";

    private static FileDescriptorProto Flight() => global::Contracts.Grpc.Flight.FlightReflection.Descriptor.ToProto();

    private static ProtoFileSnapshot FlightBaseline() => ContractSnapshots.ForProto(Flight());

    private static DescriptorProto Message(FileDescriptorProto file, string name) =>
        file.MessageType.Single(message => message.Name == name);

    private static IReadOnlyList<string> CompareFlight(Action<FileDescriptorProto> edit)
    {
        var file = Flight();
        edit(file);
        return CompatibilityRules.Compare(FlightBaseline(), ContractSnapshots.ForProto(file));
    }

    [Fact]
    public void unchanged_proto_is_compatible() => CompareFlight(_ => { }).Should().BeEmpty();

    [Fact]
    public void additive_proto_changes_are_compatible()
    {
        CompareFlight(file =>
            {
                Message(file, "FlightResponse")
                    .Field.Add(
                        new FieldDescriptorProto
                        {
                            Name = "Terminal",
                            JsonName = "Terminal",
                            Number = 13,
                            Type = FieldDescriptorProto.Types.Type.String,
                            Label = FieldDescriptorProto.Types.Label.Optional,
                        }
                    );
                file.MessageType.Add(new DescriptorProto { Name = "CancelSeatRequest" });
                file.Service[0]
                    .Method.Add(
                        new MethodDescriptorProto
                        {
                            Name = "CancelSeat",
                            InputType = ".flight.CancelSeatRequest",
                            OutputType = ".flight.ReserveSeatResult",
                        }
                    );
                file.EnumType.Single(e => e.Name == "FlightStatus")
                    .Value.Add(new EnumValueDescriptorProto { Name = "FLIGHT_STATUS_BOARDING", Number = 5 });
            })
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void removing_a_field_without_reserving_it_is_breaking() =>
        CompareFlight(file => Message(file, "FlightResponse").Field.Remove(Field(file, "FlightResponse", "Price")))
            .Should()
            .ContainSingle(v => v.Contains("FlightResponse.Price = 11") && v.Contains("removed"));

    [Fact]
    public void removing_a_field_is_breaking_even_when_its_number_and_name_are_reserved() =>
        CompareFlight(file =>
            {
                var message = Message(file, "FlightResponse");
                message.Field.Remove(Field(file, "FlightResponse", "Price"));
                message.ReservedName.Add("Price");
                message.ReservedRange.Add(new DescriptorProto.Types.ReservedRange { Start = 11, End = 12 });
            })
            .Should()
            .ContainSingle(v => v.Contains("FlightResponse.Price = 11") && v.Contains("removed"));

    [Fact]
    public void changing_proto3_optional_presence_is_breaking()
    {
        var optional = Flight();
        var message = Message(optional, "FlightResponse");
        message.OneofDecl.Add(new OneofDescriptorProto { Name = "_Price" });
        var price = Field(optional, "FlightResponse", "Price");
        price.Proto3Optional = true;
        price.OneofIndex = message.OneofDecl.Count - 1;

        CompatibilityRules
            .Compare(ContractSnapshots.ForProto(optional), FlightBaseline())
            .Should()
            .ContainSingle(v => v.Contains("FlightResponse.Price = 11") && v.Contains("presence"));
    }

    [Fact]
    public void versioned_proto_files_get_their_own_baseline() =>
        Baselines
            .ProtoFile("v2/flight.proto")
            .Should()
            .Be("grpc/v2/flight.json")
            .And.NotBe(Baselines.ProtoFile("flight.proto"));

    [Fact]
    public void changing_a_field_type_is_breaking() =>
        CompareFlight(file => Field(file, "FlightResponse", "Price").Type = FieldDescriptorProto.Types.Type.Float)
            .Should()
            .ContainSingle(v => v.Contains("FlightResponse.Price") && v.Contains("type changed"));

    [Fact]
    public void changing_a_field_number_is_breaking() =>
        CompareFlight(file => Field(file, "SeatDtoResponse", "SeatNumber").Number = 20)
            .Should()
            .Contain(v => v.Contains("SeatDtoResponse.SeatNumber = 2") && v.Contains("removed"));

    [Fact]
    public void renaming_a_field_is_breaking() =>
        CompareFlight(file =>
            {
                var field = Field(file, "ReserveSeatRequest", "SeatNumber");
                field.Name = "Seat";
                field.JsonName = "Seat";
            })
            .Should()
            .ContainSingle(v => v.Contains("ReserveSeatRequest.SeatNumber") && v.Contains("renamed"));

    [Fact]
    public void changing_field_cardinality_is_breaking() =>
        CompareFlight(file =>
                Field(file, "GetAvailableSeatsResult", "SeatDtos").Label = FieldDescriptorProto.Types.Label.Optional
            )
            .Should()
            .ContainSingle(v => v.Contains("SeatDtos") && v.Contains("cardinality"));

    [Fact]
    public void reusing_a_reserved_field_number_is_breaking()
    {
        var withReservation = Flight();
        var message = Message(withReservation, "FlightResponse");
        message.Field.Remove(message.Field.Single(f => f.Name == "Price"));
        message.ReservedName.Add("Price");
        message.ReservedRange.Add(new DescriptorProto.Types.ReservedRange { Start = 11, End = 12 });
        var baseline = ContractSnapshots.ForProto(withReservation);

        var reused = withReservation.Clone();
        Message(reused, "FlightResponse")
            .Field.Add(
                new FieldDescriptorProto
                {
                    Name = "Fare",
                    JsonName = "Fare",
                    Number = 11,
                    Type = FieldDescriptorProto.Types.Type.String,
                    Label = FieldDescriptorProto.Types.Label.Optional,
                }
            );

        CompatibilityRules
            .Compare(baseline, ContractSnapshots.ForProto(reused))
            .Should()
            .ContainSingle(v => v.Contains("reuses reserved number 11"));
    }

    [Fact]
    public void removing_an_rpc_is_breaking() =>
        CompareFlight(file =>
                file.Service[0].Method.Remove(file.Service[0].Method.Single(m => m.Name == "ReserveSeat"))
            )
            .Should()
            .ContainSingle(v => v.Contains("FlightGrpcService/ReserveSeat") && v.Contains("removed"));

    [Fact]
    public void changing_an_rpc_signature_is_breaking() =>
        CompareFlight(file =>
            {
                var method = file.Service[0].Method.Single(m => m.Name == "GetById");
                method.OutputType = ".flight.FlightResponse";
                method.ServerStreaming = true;
            })
            .Should()
            .HaveCount(2)
            .And.Contain(v => v.Contains("response type changed"))
            .And.Contain(v => v.Contains("streaming mode changed"));

    [Fact]
    public void removing_a_message_or_service_is_breaking() =>
        CompareFlight(file =>
            {
                file.MessageType.Remove(Message(file, "ReserveSeatResult"));
                file.Service[0].Name = "FlightService";
            })
            .Should()
            .Contain(v => v.Contains("message .flight.ReserveSeatResult removed"))
            .And.Contain(v => v.Contains("service .flight.FlightGrpcService removed"));

    // The pre-AB-494 bug: Booking's client protos used `package bookingFlight;` while the server
    // used `flight;`, so every cross-process call returned Unimplemented.
    [Fact]
    public void changing_the_proto_package_is_breaking() =>
        CompareFlight(file => file.Package = "bookingFlight")
            .Should()
            .Contain(v => v.Contains("package changed from 'flight' to 'bookingFlight'"));

    [Fact]
    public void removing_or_renumbering_an_enum_value_is_breaking() =>
        CompareFlight(file =>
            {
                var status = file.EnumType.Single(e => e.Name == "FlightStatus");
                status.Value.Remove(status.Value.Single(v => v.Name == "FLIGHT_STATUS_CANCELED"));
                status.Value.Single(v => v.Name == "FLIGHT_STATUS_DELAY").Number = 7;
            })
            .Should()
            .HaveCount(2)
            .And.Contain(v => v.Contains("FLIGHT_STATUS_CANCELED = 3 removed"))
            .And.Contain(v => v.Contains("FLIGHT_STATUS_DELAY renumbered from 2 to 7"));

    [Fact]
    public void provider_change_that_drops_a_field_booking_reads_breaks_the_booking_pact()
    {
        var file = Flight();
        var message = Message(file, "SeatDtoResponse");
        message.Field.Remove(message.Field.Single(f => f.Name == "SeatNumber"));
        message.ReservedName.Add("SeatNumber");
        message.ReservedRange.Add(new DescriptorProto.Types.ReservedRange { Start = 2, End = 3 });
        var provider = ContractSnapshots.ForProto(file);

        // The pact names the consumer that breaks, on top of the schema rule.
        CompatibilityRules
            .Compare(FlightBaseline(), provider)
            .Should()
            .ContainSingle(v => v.Contains("SeatNumber = 2"));
        Pacts
            .Verify(Pacts.Read<GrpcPact>("grpc/booking-to-flight.json"), provider)
            .Should()
            .ContainSingle(v => v.Contains("SeatDtoResponse.SeatNumber = 2"));
    }

    [Fact]
    public void unchanged_message_contracts_are_compatible()
    {
        var snapshot = ContractSnapshots.ForMessages(ContractSnapshots.MessagesAssembly);
        CompatibilityRules.Compare(snapshot, Baselines.Clone(snapshot)).Should().BeEmpty();
    }

    [Fact]
    public void adding_a_member_to_an_integration_event_is_compatible() =>
        CompareUserCreated(typeof(Additive.UserCreated)).Should().BeEmpty();

    [Fact]
    public void removing_renaming_or_retyping_an_integration_event_member_is_breaking() =>
        CompareUserCreated(typeof(Breaking.UserCreated))
            .Should()
            .HaveCount(4)
            .And.Contain(v => v.Contains("property PassportNumber removed or renamed"))
            .And.Contain(v => v.Contains("property Id type changed from System.Guid to System.String"))
            .And.Contain(v => v.Contains("property Name became nullable"))
            .And.Contain(v => v.Contains("property Passport added as required"));

    [Fact]
    public void adding_a_non_nullable_member_with_a_default_is_compatible() =>
        CompareUserCreated(typeof(Defaulted.UserCreated)).Should().BeEmpty();

    [Fact]
    public void default_on_a_constructor_the_serializer_does_not_use_is_ignored() =>
        CompareUserCreated(typeof(UnusedDefault.UserCreated))
            .Should()
            .ContainSingle(v => v.Contains("property Country added as required"));

    [Theory]
    [InlineData(typeof(InitializerDefault.UserCreated))]
    [InlineData(typeof(PrivateJsonConstructor.UserCreated))]
    public void defaults_from_initializers_or_a_private_json_constructor_are_recognised(Type modified) =>
        CompareUserCreated(modified).Should().BeEmpty();

    [Fact]
    public void removing_the_default_of_an_accepted_member_is_breaking()
    {
        var baseline = ContractSnapshots.ForMessages(ContractSnapshots.MessagesAssembly);
        baseline.Messages[UserCreatedName] = ContractSnapshots.ForMessage(typeof(Defaulted.UserCreated));
        var current = Baselines.Clone(baseline);
        current.Messages[UserCreatedName].Properties["Country"].HasDefault = null;

        CompatibilityRules
            .Compare(baseline, current)
            .Should()
            .ContainSingle(v => v.Contains("property Country lost its default"));
    }

    [Fact]
    public void nested_versioned_proto_baselines_are_enumerated()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "grpc", "v2"));
            File.WriteAllText(Path.Combine(root, "grpc", "flight.json"), "{}");
            File.WriteAllText(Path.Combine(root, "grpc", "v2", "flight.json"), "{}");

            Baselines
                .ExistingProtoBaselines(root)
                .Should()
                .BeEquivalentTo(Baselines.ProtoFile("flight.proto"), Baselines.ProtoFile("v2/flight.proto"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void making_an_optional_integration_event_member_required_is_breaking()
    {
        var current = ContractSnapshots.ForMessages(ContractSnapshots.MessagesAssembly);
        var baseline = Baselines.Clone(current);
        baseline.Messages[UserCreatedName].Properties["Name"].Nullable = true;

        CompatibilityRules
            .Compare(baseline, current)
            .Should()
            .ContainSingle(v => v.Contains("property Name became required"));
    }

    [Fact]
    public void moving_an_integration_event_to_another_namespace_changes_its_urn()
    {
        var baseline = ContractSnapshots.ForMessages(ContractSnapshots.MessagesAssembly);
        var current = Baselines.Clone(baseline);
        current.Messages[UserCreatedName] = ContractSnapshots.ForMessage(typeof(Additive.UserCreated));

        CompatibilityRules.Compare(baseline, current).Should().ContainSingle(v => v.Contains("URN changed"));
    }

    [Fact]
    public void removing_an_integration_event_is_breaking()
    {
        var baseline = ContractSnapshots.ForMessages(ContractSnapshots.MessagesAssembly);
        var current = Baselines.Clone(baseline);
        current.Messages.Remove(UserCreatedName);

        CompatibilityRules.Compare(baseline, current).Should().ContainSingle(v => v.Contains("UserCreated: removed"));
        Pacts
            .Verify(Pacts.Read<MessagePact>("messages/passenger-user-created.json"), current)
            .Should()
            .ContainSingle(v => v.Contains("no longer published"));
    }

    [Fact]
    public void golden_payload_is_lost_when_a_member_is_renamed()
    {
        var baseline = ContractSnapshots.ForMessages(ContractSnapshots.MessagesAssembly).Messages[UserCreatedName];
        var sample = GoldenSamples.Create(typeof(BuildingBlocks.Contracts.EventBus.Messages.UserCreated));

        GoldenSamples.Verify(UserCreatedName, baseline, typeof(Additive.UserCreated), sample).Should().BeEmpty();
        GoldenSamples
            .Verify(UserCreatedName, baseline, typeof(Breaking.UserCreated), sample)
            .Should()
            .Contain(v => v.Contains("UserCreated.PassportNumber"));
    }

    private static FieldDescriptorProto Field(FileDescriptorProto file, string message, string field) =>
        Message(file, message).Field.Single(f => f.Name == field);

    // Compares the real UserCreated against a modified copy, ignoring the URN (namespaces differ).
    private static IReadOnlyList<string> CompareUserCreated(Type modified)
    {
        var baseline = ContractSnapshots.ForMessages(ContractSnapshots.MessagesAssembly);
        var current = Baselines.Clone(baseline);
        var contract = ContractSnapshots.ForMessage(modified);
        contract.Urn = baseline.Messages[UserCreatedName].Urn;
        current.Messages[UserCreatedName] = contract;
        return CompatibilityRules.Compare(baseline, current);
    }
}
