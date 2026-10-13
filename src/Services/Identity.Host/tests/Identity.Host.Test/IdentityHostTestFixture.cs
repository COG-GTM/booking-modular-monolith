using BuildingBlocks.MassTransit;
using BuildingBlocks.TestBase;
using Identity.Data;
using Xunit;

namespace Identity.Host.Test;

// Boots the standalone Identity.Host (not the monolith) against Testcontainers Postgres + RabbitMQ.
public class IdentityHostTestFixture : TestWriteFixture<Program, IdentityContext>
{
    public const string ServiceName = "identity-host-it";
    public const string IssuerUri = "http://identity.test";

    protected override IEnumerable<KeyValuePair<string, string?>> AdditionalAppSettings() =>
        [
            new("MessageBroker:TransportType", nameof(TransportType.RabbitMq)),
            new("MessageBroker:ServiceName", ServiceName),
            new("MessageBroker:UseInbox", "true"),
            new("AuthOptions:IssuerUri", IssuerUri),
        ];
}

[CollectionDefinition(Name)]
public class IdentityHostTestCollection : ICollectionFixture<IdentityHostTestFixture>
{
    public const string Name = "Identity Host Integration Test";
}

[Collection(IdentityHostTestCollection.Name)]
public abstract class IdentityHostTestBase(IdentityHostTestFixture fixture)
    : TestWriteBase<Program, IdentityContext>(fixture)
{
    protected new IdentityHostTestFixture Fixture { get; } = fixture;
}
