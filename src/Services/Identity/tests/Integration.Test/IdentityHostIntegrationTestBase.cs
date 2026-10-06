using BuildingBlocks.Constants;
using BuildingBlocks.TestBase;
using FluentAssertions;
using global::Identity.Identity.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IdentityContext = global::Identity.Data.IdentityContext;
using IdentityHostProgram = global::Identity.Host.Program;

namespace Identity.Host.Integration.Test;

[Collection(IdentityHostIntegrationTestCollection.Name)]
public abstract class IdentityHostIntegrationTestBase : TestWriteBase<IdentityHostProgram, IdentityContext>
{
    protected IdentityHostIntegrationTestBase(
        TestWriteFixture<IdentityHostProgram, IdentityContext> integrationTestFactory
    )
        : base(integrationTestFactory) { }

    protected async Task EnsureUserRoleExistsAsync()
    {
        await using var scope = Fixture.ServiceProvider.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<Role>>();
        var roleName = IdentityConstant.Role.User;

        if (!await roleManager.RoleExistsAsync(roleName))
        {
            var result = await roleManager.CreateAsync(new Role { Name = roleName });
            result.Succeeded.Should().BeTrue();
        }
    }
}

[CollectionDefinition(Name)]
public class IdentityHostIntegrationTestCollection
    : ICollectionFixture<TestWriteFixture<IdentityHostProgram, IdentityContext>>
{
    public const string Name = "Identity Host Integration Test";
}
