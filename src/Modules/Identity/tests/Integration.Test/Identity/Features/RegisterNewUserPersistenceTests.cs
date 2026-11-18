using System;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.TestBase;
using FluentAssertions;
using global::Identity.Data;
using global::Identity.Identity.Exceptions;
using global::Identity.Identity.Models;
using Integration.Test.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Integration.Test.Identity.Features;

public class RegisterNewUserPersistenceTests : IdentityIntegrationTestBase
{
    public RegisterNewUserPersistenceTests(TestWriteFixture<Program, IdentityContext> integrationTestFactory)
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_enforce_named_unique_passport_index_in_database()
    {
        using var scope = Fixture.ServiceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityContext>();
        dbContext.Users.Add(
            new User
            {
                Id = Guid.NewGuid(),
                FirstName = "First",
                LastName = "User",
                UserName = "FirstUser",
                NormalizedUserName = "FIRSTUSER",
                PassPortNumber = "AB1234567",
            }
        );
        await dbContext.SaveChangesAsync();
        dbContext.Users.Add(
            new User
            {
                Id = Guid.NewGuid(),
                FirstName = "Second",
                LastName = "User",
                UserName = "SecondUser",
                NormalizedUserName = "SECONDUSER",
                PassPortNumber = "AB1234567",
            }
        );

        var act = () => dbContext.SaveChangesAsync();

        var exception = await act.Should().ThrowAsync<DbUpdateException>();
        var postgresException = exception.Which.InnerException.Should().BeOfType<PostgresException>().Subject;
        postgresException.SqlState.Should().Be("23505");
        postgresException.ConstraintName.Should().Be("PassportNumberIndex");
    }

    [Fact]
    public async Task should_not_create_user_or_role_assignment_when_username_is_duplicate()
    {
        var command = new FakeRegisterNewUserCommand().Generate();
        var firstUser = await Fixture.SendAsync(command);
        int userCount;
        int roleAssignmentCount;
        using (var scope = Fixture.ServiceProvider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityContext>();
            userCount = await dbContext.Users.CountAsync();
            roleAssignmentCount = await dbContext.UserRoles.CountAsync();
            (await dbContext.UserRoles.AnyAsync(role => role.UserId == firstUser.Id)).Should().BeTrue();
        }

        var secondCommand = command with { PassportNumber = "CD7654321", Email = "second@test.com" };
        var act = () => Fixture.SendAsync(secondCommand);

        await act.Should().ThrowAsync<RegisterIdentityUserException>();

        using var verificationScope = Fixture.ServiceProvider.CreateScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<IdentityContext>();
        (await verificationContext.Users.CountAsync()).Should().Be(userCount);
        (await verificationContext.UserRoles.CountAsync()).Should().Be(roleAssignmentCount);
        (await verificationContext.Users.AnyAsync(user => user.PassPortNumber == secondCommand.PassportNumber))
            .Should()
            .BeFalse();
        (await verificationContext.UserRoles.AnyAsync(role => role.UserId == firstUser.Id)).Should().BeTrue();
    }
}
