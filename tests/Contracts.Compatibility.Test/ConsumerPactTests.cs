namespace Contracts.Compatibility.Test;

using Contracts.Compatibility.Test.Model;
using FluentAssertions;
using Xunit;

public class ConsumerPactTests
{
    private static readonly MessageContractsSnapshot Messages = ContractSnapshots.ForMessages(
        ContractSnapshots.MessagesAssembly
    );

    private static readonly IReadOnlyDictionary<string, ProtoFileSnapshot> Protos = ContractSnapshots
        .ProtoFiles(ContractSnapshots.GrpcAssembly)
        .Select(ContractSnapshots.ForProto)
        .ToDictionary(file => file.File, StringComparer.Ordinal);

    public static TheoryData<string> GrpcPactFiles() => ToTheory(Pacts.GrpcPactFiles());

    public static TheoryData<string> MessagePactFiles() => ToTheory(Pacts.MessagePactFiles());

    [Theory]
    [MemberData(nameof(GrpcPactFiles))]
    public void grpc_provider_satisfies_consumer_pact(string pactFile)
    {
        var pact = Pacts.Read<GrpcPact>(pactFile);
        Protos.Should().ContainKey(pact.ProtoFile);
        Pacts.Verify(pact, Protos[pact.ProtoFile]).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(MessagePactFiles))]
    public void message_producer_satisfies_consumer_pact(string pactFile)
    {
        var pact = Pacts.Read<MessagePact>(pactFile);
        Pacts.Verify(pact, Messages).Should().BeEmpty();
    }

    [Fact]
    public void every_integration_event_consumer_declares_a_pact()
    {
        var discovered = Pacts.DiscoverConsumers(
            [
                typeof(Identity.IdentityRoot).Assembly,
                typeof(Flight.FlightRoot).Assembly,
                typeof(Passenger.PassengerRoot).Assembly,
                typeof(Booking.BookingRoot).Assembly,
            ],
            Messages.Messages.Keys
        );
        var declared = Pacts
            .MessagePactFiles()
            .Select(Pacts.Read<MessagePact>)
            .Select(pact => $"{pact.Consumer}|{pact.MessageType}")
            .ToHashSet(StringComparer.Ordinal);

        discovered.Should().NotBeEmpty();
        discovered
            .Except(declared)
            .Should()
            .BeEmpty("add a Pacts/messages/*.json file listing the members each new consumer reads");
    }

    private static TheoryData<string> ToTheory(IEnumerable<string> items)
    {
        var data = new TheoryData<string>();
        foreach (var item in items)
        {
            data.Add(item);
        }

        return data;
    }
}
