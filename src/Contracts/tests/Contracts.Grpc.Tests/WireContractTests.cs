using System.Text;
using FluentAssertions;
using Google.Protobuf.Reflection;
using Xunit;

namespace Contracts.Grpc.Tests;

/// <summary>
/// Guards the wire contract: packages, service/method paths, field numbers/types and enum values must not change
/// (ADR 0102). Additive changes update contract.snapshot.txt in the same PR; anything else is a breaking change.
/// </summary>
public class WireContractTests
{
    private static readonly FileDescriptor[] Contracts = [Flight.FlightReflection.Descriptor, Passenger.PassengerReflection.Descriptor];

    [Fact]
    public void grpc_method_paths_are_the_canonical_provider_paths()
    {
        Flight.FlightGrpcService.Descriptor.FullName.Should().Be("flight.FlightGrpcService");
        Passenger.PassengerGrpcService.Descriptor.FullName.Should().Be("passenger.PassengerGrpcService");

        Flight.FlightGrpcService.Descriptor.Methods.Select(m => m.Name)
            .Should().Equal("GetById", "GetAvailableSeats", "ReserveSeat");
        Passenger.PassengerGrpcService.Descriptor.Methods.Select(m => m.Name).Should().Equal("GetById");
    }

    [Fact]
    public void wire_contract_matches_the_committed_snapshot()
    {
        if (Environment.GetEnvironmentVariable("UPDATE_CONTRACT_SNAPSHOT") == "1")
        {
            File.WriteAllText(Path.Combine(RepositoryRoot(), "src", "Contracts", "tests", "Contracts.Grpc.Tests", "contract.snapshot.txt"), Describe());
            return;
        }

        var expected = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "contract.snapshot.txt")).ReplaceLineEndings("\n");

        Describe().Should().Be(expected, "breaking proto changes need a new package (see ADR 0102); additive ones update the snapshot");
    }

    [Fact]
    public void proto_files_exist_only_in_the_shared_contracts_package()
    {
        var root = RepositoryRoot();
        var contracts = Path.Combine(root, "src", "Contracts", "Contracts.Grpc", "Protos") + Path.DirectorySeparatorChar;

        var protos = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.proto", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        protos.Should().NotBeEmpty();
        protos.Should().OnlyContain(p => p.StartsWith(contracts, StringComparison.Ordinal), "every .proto must live in src/Contracts/Contracts.Grpc/Protos");
    }

    internal static string Describe()
    {
        var sb = new StringBuilder();
        foreach (var file in Contracts)
        {
            sb.Append("file ").Append(file.Name).Append(" package ").Append(file.Package).Append('\n');
            foreach (var service in file.Services)
            {
                foreach (var method in service.Methods)
                {
                    sb.Append("  rpc /").Append(service.FullName).Append('/').Append(method.Name)
                        .Append(' ').Append(method.InputType.FullName).Append(" -> ").Append(method.OutputType.FullName).Append('\n');
                }
            }

            foreach (var message in file.MessageTypes.OrderBy(m => m.FullName, StringComparer.Ordinal))
            {
                sb.Append("  message ").Append(message.FullName).Append('\n');
                foreach (var field in message.Fields.InFieldNumberOrder())
                {
                    var type = field.FieldType switch
                    {
                        FieldType.Message => field.MessageType.FullName,
                        FieldType.Enum => field.EnumType.FullName,
                        _ => field.FieldType.ToString().ToLowerInvariant(),
                    };
                    sb.Append("    ").Append(field.FieldNumber).Append(' ').Append(field.IsRepeated ? "repeated " : string.Empty)
                        .Append(type).Append(' ').Append(field.Name).Append('\n');
                }
            }

            foreach (var e in file.EnumTypes.OrderBy(x => x.FullName, StringComparer.Ordinal))
            {
                sb.Append("  enum ").Append(e.FullName).Append('\n');
                foreach (var value in e.Values)
                    sb.Append("    ").Append(value.Number).Append(' ').Append(value.Name).Append('\n');
            }
        }

        return sb.ToString();
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "booking-modular-monolith.sln")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
