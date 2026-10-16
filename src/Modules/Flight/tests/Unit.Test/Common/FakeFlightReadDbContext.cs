namespace Unit.Test.Common;

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BuildingBlocks.Mongo;
using global::Flight.Data;
using global::Flight.Flights.Models;
using global::Flight.Seats.Models;
using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using NSubstitute;

public static class FakeFlightReadDbContext
{
    public static FlightReadDbContext Create(
        IEnumerable<FlightReadModel>? flights = null,
        IEnumerable<SeatReadModel>? seats = null,
        Action<PipelineDefinition<FlightReadModel, FlightReadModel>>? captureFlightPipeline = null
    )
    {
        var databaseName = "flight-unit";
        var context = new FlightReadDbContext(
            Options.Create(
                new MongoOptions { ConnectionString = "mongodb://localhost:27017", DatabaseName = databaseName }
            )
        );

        if (flights is not null)
        {
            SetCollection(
                context,
                nameof(FlightReadDbContext.Flight),
                CreateCollection(flights, databaseName, "flight", captureFlightPipeline)
            );
        }

        if (seats is not null)
        {
            SetCollection(context, nameof(FlightReadDbContext.Seat), CreateCollection(seats, databaseName, "seat"));
        }

        return context;
    }

    private static IMongoCollection<TDocument> CreateCollection<TDocument>(
        IEnumerable<TDocument> documents,
        string databaseName,
        string collectionName,
        Action<PipelineDefinition<TDocument, TDocument>>? capturePipeline = null
    )
    {
        var client = new MongoClient("mongodb://localhost:27017");
        var collection = Substitute.For<IMongoCollection<TDocument>>();
        collection.Database.Returns(client.GetDatabase(databaseName));
        collection.CollectionNamespace.Returns(
            new CollectionNamespace(new DatabaseNamespace(databaseName), collectionName)
        );
        collection.Settings.Returns(new MongoCollectionSettings());
        collection.DocumentSerializer.Returns(BsonSerializer.LookupSerializer<TDocument>());

        var cursor = Substitute.For<IAsyncCursor<TDocument>>();
        cursor.Current.Returns(new List<TDocument>(documents));
        cursor.MoveNextAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(true), Task.FromResult(false));
        collection
            .AggregateAsync<TDocument>(
                Arg.Do<PipelineDefinition<TDocument, TDocument>>(pipeline => capturePipeline?.Invoke(pipeline)),
                Arg.Any<AggregateOptions>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(cursor);

        return collection;
    }

    private static void SetCollection(FlightReadDbContext context, string propertyName, object value)
    {
        var backingField = typeof(FlightReadDbContext).GetField(
            $"<{propertyName}>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        backingField!.SetValue(context, value);
    }
}
