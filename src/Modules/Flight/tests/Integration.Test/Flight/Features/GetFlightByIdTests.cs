using System;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.TestBase;
using Flight;
using Flight.Data;
using FluentAssertions;
using Grpc.Core;
using Integration.Test.Fakes;
using Xunit;

namespace Integration.Test.Flight.Features;

using global::Flight.Flights.Exceptions;
using global::Flight.Flights.Features.GettingFlightById.V1;

public class GetFlightByIdTests : FlightIntegrationTestBase
{
    public GetFlightByIdTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFactory)
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_retrive_a_flight_by_id_currectly()
    {
        //Arrange
        var command = new FakeCreateFlightMongoCommand().Generate();

        await Fixture.SendAsync(command);

        var query = new GetFlightById(command.Id);

        // Act
        var response = await Fixture.SendAsync(query);

        // Assert
        response.Should().NotBeNull();
        response?.FlightDto?.Id.Should().Be(command.Id);
    }

    [Fact]
    public async Task should_retrive_a_flight_by_id_from_grpc_service()
    {
        //Arrange
        var command = new FakeCreateFlightMongoCommand().Generate();

        await Fixture.SendAsync(command);

        var flightGrpcClient = new FlightGrpcService.FlightGrpcServiceClient(Fixture.Channel);

        // Act
        var response = await flightGrpcClient
            .GetByIdAsync(new GetByIdRequest { Id = command.Id.ToString() })
            .ResponseAsync;

        // Assert
        response?.Should().NotBeNull();
        response?.FlightDto.Id.Should().Be(command.Id.ToString());
    }

    [Fact]
    public async Task should_throw_flight_not_found_exception_for_unknown_id()
    {
        //Arrange
        var query = new GetFlightById(Guid.NewGuid());

        // Act
        Func<Task> act = () => Fixture.SendAsync(query);

        // Assert
        await act.Should().ThrowAsync<FlightNotFountException>();
    }

    [Fact]
    public async Task should_throw_flight_not_found_exception_for_soft_deleted_flight()
    {
        //Arrange
        var command = new FakeCreateFlightMongoCommand().Generate() with
        {
            IsDeleted = true,
        };

        await Fixture.SendAsync(command);

        var query = new GetFlightById(command.Id);

        // Act
        Func<Task> act = () => Fixture.SendAsync(query);

        // Assert
        await act.Should().ThrowAsync<FlightNotFountException>();
    }

    [Fact]
    public async Task should_throw_rpc_exception_for_unknown_id_from_grpc_service()
    {
        //Arrange
        var flightGrpcClient = new FlightGrpcService.FlightGrpcServiceClient(Fixture.Channel);

        // Act
        Func<Task> act = () =>
            flightGrpcClient.GetByIdAsync(new GetByIdRequest { Id = Guid.NewGuid().ToString() }).ResponseAsync;

        // Assert
        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.Internal);
        exception.Which.Status.Detail.Should().Be("Flight not found!");
    }
}
