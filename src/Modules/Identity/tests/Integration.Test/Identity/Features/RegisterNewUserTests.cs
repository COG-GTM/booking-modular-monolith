using System.Threading.Tasks;
using Api;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Identity.Data;
using Identity.Identity.Exceptions;
using Integration.Test.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Integration.Test.Identity.Features;

public class RegisterNewUserTests : IdentityIntegrationTestBase
{
    public RegisterNewUserTests(TestWriteFixture<Program, IdentityContext> integrationTestFactory)
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_create_new_user_to_db_and_publish_message_to_broker()
    {
        // Arrange
        var command = new FakeRegisterNewUserCommand().Generate() with
        {
            PassportNumber = " ab1234567 ",
        };

        // Act
        var response = await Fixture.SendAsync(command);

        // Assert
        response?.Should().NotBeNull();
        response?.Username.Should().Be(command.Username);
        response?.PassportNumber.Should().Be("AB1234567");

        using var scope = Fixture.ServiceProvider.CreateScope();
        var storedUser = await scope
            .ServiceProvider.GetRequiredService<IdentityContext>()
            .Users.SingleAsync(user => user.Id == response!.Id);
        storedUser.PassPortNumber.Should().Be("AB1234567");

        (await Fixture.WaitForPublishing<UserCreated>()).Should().Be(true);
    }

    [Fact]
    public async Task should_reject_second_registration_with_same_passport_number()
    {
        var firstCommand = new FakeRegisterNewUserCommand().Generate();
        await Fixture.SendAsync(firstCommand);

        var secondCommand = firstCommand with { Username = "AnotherUser", Email = "another@test.com" };

        var act = () => Fixture.SendAsync(secondCommand);

        await act.Should().ThrowAsync<PassportNumberAlreadyRegisteredException>();
    }

    [Fact]
    public async Task should_reject_passport_number_differing_only_by_case_or_whitespace()
    {
        var firstCommand = new FakeRegisterNewUserCommand().Generate() with { PassportNumber = "AB1234567" };
        await Fixture.SendAsync(firstCommand);

        var secondCommand = firstCommand with
        {
            Username = "AnotherUser",
            Email = "another@test.com",
            PassportNumber = " ab1234567 ",
        };

        var act = () => Fixture.SendAsync(secondCommand);

        await act.Should().ThrowAsync<PassportNumberAlreadyRegisteredException>();
    }
}
