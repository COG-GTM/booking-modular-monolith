using FluentAssertions;
using Microsoft.Extensions.Options;
using Passenger.Data;
using Passenger.Passengers.Features.CompletingRegisterPassenger.V1;
using Passenger.Passengers.Features.GettingPassengerById.V1;
using Unit.Test.Common;
using Xunit;

namespace Unit.Test.Passengers.Features.GettingPassengerById;

public class PassengerReadHandlerTests
{
    [Fact]
    public async Task Get_by_id_handler_rejects_a_null_request_before_querying_mongo()
    {
        using var db = CreateReadDbContext();
        var handler = new GetPassengerByIdHandler(MapperFactory.Create(), db);

        await FluentActions
            .Invoking(() => handler.Handle(null!, CancellationToken.None))
            .Should()
            .ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Complete_registration_mongo_handler_rejects_a_null_request_before_querying_mongo()
    {
        using var db = CreateReadDbContext();
        var handler = new CompleteRegisterPassengerMongoHandler(db, MapperFactory.Create());

        await FluentActions
            .Invoking(() => handler.Handle(null!, CancellationToken.None))
            .Should()
            .ThrowAsync<ArgumentNullException>();
    }

    private static PassengerReadDbContext CreateReadDbContext() =>
        new(
            Options.Create(
                new BuildingBlocks.Mongo.MongoOptions
                {
                    ConnectionString = "mongodb://localhost:27017",
                    DatabaseName = "x",
                }
            )
        );
}
