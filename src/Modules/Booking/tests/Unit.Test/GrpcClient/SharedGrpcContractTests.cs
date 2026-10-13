using Contracts.Grpc.Flight;
using Contracts.Grpc.Passenger;
using FluentAssertions;
using Xunit;

namespace Unit.Test.GrpcClient;

public class SharedGrpcContractTests
{
    [Fact]
    public void proto_packages_match_the_server_routes()
    {
        FlightGrpcService.Descriptor.FullName.Should().Be("flight.FlightGrpcService");
        PassengerGrpcService.Descriptor.FullName.Should().Be("passenger.PassengerGrpcService");
    }

    [Fact]
    public void protos_exist_only_in_contracts_grpc()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "booking-modular-monolith.sln")))
        {
            root = root.Parent;
        }

        root.Should().NotBeNull();
        var sharedProtos = Path.Combine(root!.FullName, "src", "Contracts", "Contracts.Grpc", "Protos");

        var protos = Directory
            .EnumerateFiles(Path.Combine(root.FullName, "src"), "*.proto", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains(
                    $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal
                )
            )
            .Where(path =>
                !path.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal
                )
            )
            .ToList();

        protos.Should().NotBeEmpty();
        protos.Should().OnlyContain(path => path.StartsWith(sharedProtos, StringComparison.Ordinal));
    }
}
