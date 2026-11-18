using System;
using System.Linq;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.TestBase;
using FluentAssertions;
using global::Identity.Identity.Exceptions;
using Identity.Data;
using Integration.Test.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Integration.Test.Identity.Features;

public class RegisterNewUserPassportUniquenessTests : IdentityIntegrationTestBase
{
    public RegisterNewUserPassportUniquenessTests(TestWriteFixture<Program, IdentityContext> integrationTestFactory)
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_allow_only_one_of_two_concurrent_registrations_with_the_same_passport_number()
    {
        var first = new FakeRegisterNewUserCommand().Generate() with
        {
            Username = "ConcurrentUser1",
            Email = "concurrent1@test.com",
            PassportNumber = "CC1234567",
        };
        var second = first with
        {
            Username = "ConcurrentUser2",
            Email = "concurrent2@test.com",
            PassportNumber = " cc1234567 ",
        };

        var outcomes = await Task.WhenAll(CaptureAsync(first), CaptureAsync(second));

        outcomes.Count(e => e is null).Should().Be(1);
        outcomes
            .Where(e => e is not null)
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<PassportNumberAlreadyRegisteredException>();

        using var scope = Fixture.ServiceProvider.CreateScope();
        var users = await scope
            .ServiceProvider.GetRequiredService<IdentityContext>()
            .Users.Where(u => u.PassPortNumber == "CC1234567")
            .ToListAsync();
        users.Should().ContainSingle();
    }

    private async Task<Exception?> CaptureAsync(
        global::Identity.Identity.Features.RegisteringNewUser.V1.RegisterNewUser command
    )
    {
        try
        {
            await Fixture.SendAsync(command);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
