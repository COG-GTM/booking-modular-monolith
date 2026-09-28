using BuildingBlocks.TestBase;
using Flight.Data;
using Xunit;

namespace Contract.Test.Grpc;

[Collection(FlightGrpcContractCollection.Name)]
public abstract class FlightGrpcContractTestBase : TestBase<Flight.Api.Program, FlightDbContext, FlightReadDbContext>
{
    protected FlightGrpcContractTestBase(TestFixture<Flight.Api.Program, FlightDbContext, FlightReadDbContext> fixture)
        : base(fixture) { }
}

[CollectionDefinition(Name)]
public class FlightGrpcContractCollection
    : ICollectionFixture<TestFixture<Flight.Api.Program, FlightDbContext, FlightReadDbContext>>
{
    public const string Name = "Booking -> Flight gRPC Contract Test";
}
