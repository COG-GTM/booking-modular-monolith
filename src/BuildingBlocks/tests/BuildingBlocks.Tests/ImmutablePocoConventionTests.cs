using BuildingBlocks.Mongo;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using Xunit;

namespace BuildingBlocks.Tests;

public sealed class ImmutablePocoConventionTests
{
    [Fact]
    public void round_trips_read_only_models_with_private_and_public_matching_constructors()
    {
        ConventionRegistry.Register(
            "ImmutablePocoConventionTests",
            new ConventionPack { new ImmutablePocoConvention() },
            type => type == typeof(PrivateConstructorModel) || type == typeof(PublicConstructorModel)
        );

        var privateModel = PrivateConstructorModel.Create("private", 1);
        var privateRoundTrip = BsonSerializer.Deserialize<PrivateConstructorModel>(privateModel.ToBson());

        privateRoundTrip.Should().BeEquivalentTo(privateModel);

        var publicModel = new PublicConstructorModel("public", 2);
        var publicRoundTrip = BsonSerializer.Deserialize<PublicConstructorModel>(publicModel.ToBson());

        publicRoundTrip.Should().BeEquivalentTo(publicModel);
    }

    private sealed class PrivateConstructorModel
    {
        private PrivateConstructorModel(string name, int revision)
        {
            Name = name;
            Revision = revision;
        }

        public string Name { get; }
        public int Revision { get; }

        public static PrivateConstructorModel Create(string name, int revision) => new(name, revision);
    }

    private sealed class PublicConstructorModel
    {
        public PublicConstructorModel(string name, int revision)
        {
            Name = name;
            Revision = revision;
        }

        public string Name { get; }
        public int Revision { get; }
    }
}
