using System.Threading.Tasks;
using BuildingBlocks.Core;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Integration.Test;

using global::Identity;
using global::Identity.Data;

[Collection(IdentityApiTestCollection.Name)]
public class IdentityApiHostTests : TestWriteBase<Identity.Api.Program, IdentityContext>
{
    public IdentityApiHostTests(TestWriteFixture<Identity.Api.Program, IdentityContext> integrationTestFixture)
        : base(integrationTestFixture) { }

    [Fact]
    public async Task should_serve_root_endpoint_with_service_name()
    {
        var response = await Fixture.HttpClient.GetStringAsync("/");

        response.Should().Be("Identity-Service");
    }

    [Fact]
    public void should_register_identity_event_mapper_as_the_only_event_mapper()
    {
        using var scope = Fixture.ServiceProvider.CreateScope();

        var eventMappers = scope.ServiceProvider.GetServices<IEventMapper>();

        eventMappers.Should().ContainSingle().Which.Should().BeOfType<IdentityEventMapper>();
    }
}

[CollectionDefinition(Name)]
public class IdentityApiTestCollection : ICollectionFixture<TestWriteFixture<Identity.Api.Program, IdentityContext>>
{
    public const string Name = "IdentityApi Integration Test";
}
