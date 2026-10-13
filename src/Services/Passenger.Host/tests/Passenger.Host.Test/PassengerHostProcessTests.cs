using System.Net;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Mongo;
using Contracts.Grpc.Passenger;
using FluentAssertions;
using Grpc.Net.Client;
using MassTransit;
using Microsoft.Extensions.Options;
using Npgsql;
using Passenger.Data;
using Passenger.Passengers.Models;
using Xunit;
using PassengerType = Passenger.Passengers.Enums.PassengerType;

namespace Passenger.Host.Test;

[Collection(PassengerHostProcessCollection.Name)]
public class PassengerHostProcessTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(60);

    private readonly PassengerHostProcessFixture _fixture;

    public PassengerHostProcessTests(PassengerHostProcessFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task standalone_host_should_serve_rest_and_require_authentication()
    {
        using var http = new HttpClient { BaseAddress = _fixture.HttpAddress };

        (await http.GetStringAsync("/")).Should().Be("Passenger-Service");
        (await http.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await http.GetAsync($"/api/v1.0/passenger/{Guid.NewGuid()}"))
            .StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task user_created_published_to_broker_should_be_consumed_by_standalone_host()
    {
        var passportNumber = $"X{Random.Shared.Next(10_000_000, 99_999_999)}";

        var publisher = _fixture.CreateExternalPublisher();
        await publisher.StartAsync();
        try
        {
            await publisher.Publish(new UserCreated(NewId.NextGuid(), "Process User", passportNumber));
        }
        finally
        {
            await publisher.StopAsync();
        }

        var name = await TestWait.UntilAsync(
            async () =>
            {
                await using var connection = new NpgsqlConnection(_fixture.PassengerConnectionString);
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand(
                    "SELECT name FROM passenger WHERE passport_number = @passport",
                    connection
                );
                command.Parameters.AddWithValue("passport", passportNumber);
                return await command.ExecuteScalarAsync() as string;
            },
            WaitTimeout
        );

        name.Should().Be("Process User", _fixture.Output);
    }

    [Fact]
    public async Task grpc_get_by_id_should_be_served_cross_process_on_http2_port()
    {
        var passengerId = NewId.NextGuid();
        var readContext = new PassengerReadDbContext(
            Options.Create(
                new MongoOptions
                {
                    ConnectionString = _fixture.MongoConnectionString,
                    DatabaseName = PassengerHostProcessFixture.ReadDatabase,
                }
            )
        );
        await readContext.Passenger.InsertOneAsync(
            new PassengerReadModel
            {
                Id = NewId.NextGuid(),
                PassengerId = passengerId,
                Name = "Cross Process",
                PassportNumber = "C12345678",
                PassengerType = PassengerType.Baby,
                Age = 2,
                IsDeleted = false,
            }
        );

        using var channel = GrpcChannel.ForAddress(_fixture.GrpcAddress);
        var client = new PassengerGrpcService.PassengerGrpcServiceClient(channel);
        var response = await client.GetByIdAsync(new GetByIdRequest { Id = passengerId.ToString() });

        response.PassengerDto.Id.Should().Be(passengerId.ToString());
        response.PassengerDto.Name.Should().Be("Cross Process");
        response.PassengerDto.PassengerType.Should().Be(Contracts.Grpc.Passenger.PassengerType.Baby);
    }
}
