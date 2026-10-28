using System.Text.Json;
using Xunit;

namespace Contracts.Grpc.Test;

public sealed class GrpcPactTests
{
    private static readonly JsonSerializerOptions PactJsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Theory]
    [MemberData(nameof(Pacts))]
    public void Grpc_pact_matches_the_current_descriptor(string pactPath)
    {
        var pact =
            JsonSerializer.Deserialize<GrpcPact>(File.ReadAllText(pactPath), PactJsonOptions)
            ?? throw new InvalidOperationException($"Invalid gRPC pact: {pactPath}");
        var file = ProtoBaselineTests
            .GetCurrentSnapshots()
            .Single(snapshot => pact.Service.StartsWith(snapshot.Package + ".", StringComparison.Ordinal));
        var serviceName = pact.Service[(file.Package.Length + 1)..];
        var service = file.Services.Single(candidate => candidate.Name == serviceName);
        var messages = file.Messages.ToDictionary(message => message.FullName, StringComparer.Ordinal);

        foreach (var rpc in pact.Rpcs)
        {
            var method = service.Methods.SingleOrDefault(candidate => candidate.Name == rpc.Name);
            Assert.NotNull(method);
            Assert.Equal(rpc.Request, method!.InputType.TrimStart('.'));
            Assert.Equal(rpc.Response, method.OutputType.TrimStart('.'));

            var request = messages[rpc.Request];
            foreach (var expected in rpc.RequestFields)
            {
                var field = request.Fields.SingleOrDefault(candidate => candidate.Name == expected.Name);
                AssertField(expected.Name, expected.Number, expected.Type, field);
            }

            var response = messages[rpc.Response];
            foreach (var expected in rpc.ResponseFields)
                AssertPathField(messages, response, expected);
        }
    }

    public static IEnumerable<object[]> Pacts =>
        Directory
            .EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Pacts"), "*.json")
            .Order(StringComparer.Ordinal)
            .Select(path => new object[] { path });

    private static void AssertPathField(
        IReadOnlyDictionary<string, ProtoMessageSnapshot> messages,
        ProtoMessageSnapshot root,
        PactField expected
    )
    {
        var parts = expected.Path.Split('.');
        var current = root;
        for (var index = 0; index < parts.Length; index++)
        {
            var field = current.Fields.SingleOrDefault(candidate => candidate.Name == parts[index]);
            Assert.NotNull(field);
            if (index == parts.Length - 1)
            {
                AssertField(expected.Path, expected.Number, expected.Type, field);
                return;
            }

            current = messages[field!.TypeName.TrimStart('.')];
        }
    }

    private static void AssertField(string _, int number, string type, ProtoFieldSnapshot? field)
    {
        Assert.NotNull(field);
        Assert.Equal(number, field!.Number);
        Assert.Equal(type, field.Type.ToLowerInvariant());
    }

    private sealed record GrpcPact(string Consumer, string Provider, string Service, RpcPact[] Rpcs);

    private sealed record RpcPact(
        string Name,
        string Request,
        string Response,
        PactField[] RequestFields,
        PactField[] ResponseFields
    );

    private sealed record PactField(string Name, int Number, string Type)
    {
        public string Path { get; init; } = Name;
    }
}
