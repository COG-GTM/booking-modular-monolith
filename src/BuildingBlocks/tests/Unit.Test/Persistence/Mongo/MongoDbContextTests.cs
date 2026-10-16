using BuildingBlocks.Mongo;
using FluentAssertions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using Xunit;

namespace BuildingBlocks.Unit.Test.Persistence;

public class MongoDbContextTests
{
    private static PersistMongoDbContext CreateContext() =>
        new(Options.Create(new MongoOptions { ConnectionString = "mongodb://localhost", DatabaseName = "test" }));

    [Fact]
    public void get_collection_defaults_to_lower_cased_type_name()
    {
        using var context = CreateContext();

        context
            .GetCollection<PersistMongoFlight>()
            .CollectionNamespace.CollectionName.Should()
            .Be("persistmongoflight");
    }

    [Fact]
    public void get_collection_honours_explicit_name()
    {
        using var context = CreateContext();

        context.GetCollection<PersistMongoFlight>("flights").CollectionNamespace.CollectionName.Should().Be("flights");
    }

    [Fact]
    public void immutable_poco_convention_round_trips_get_only_properties()
    {
        ConventionRegistry.Register(
            "persistence-immutable-poco",
            new ConventionPack { new ImmutablePocoConvention() },
            t => t == typeof(PersistImmutablePoco)
        );
        var poco = new PersistImmutablePoco(Guid.NewGuid(), "alpha");

        var doc = poco.ToBsonDocument();
        var back = BsonSerializer.Deserialize<PersistImmutablePoco>(doc);

        back.Id.Should().Be(poco.Id);
        back.Name.Should().Be("alpha");
    }

    [Fact]
    public void immutable_poco_convention_maps_no_creator_when_ctor_params_do_not_match()
    {
        ConventionRegistry.Register(
            "persistence-immutable-poco-mismatch",
            new ConventionPack { new ImmutablePocoConvention() },
            t => t == typeof(PersistMismatchedCtorPoco)
        );
        var classMap = BsonClassMap.LookupClassMap(typeof(PersistMismatchedCtorPoco));

        // No public member named "other" matches the ctor parameter, so no creator is mapped.
        classMap.CreatorMaps.Should().BeEmpty();
    }
}
