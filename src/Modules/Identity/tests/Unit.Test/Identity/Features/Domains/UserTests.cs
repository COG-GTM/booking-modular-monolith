using FluentAssertions;
using Identity.Identity.Models;
using Unit.Test.Common;
using Xunit;

namespace Unit.Test.Identity.Features.Domains;

[Collection(nameof(UnitTestFixture))]
public class UserTests
{
    private readonly UnitTestFixture _fixture;

    public UserTests(UnitTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task can_persist_user_with_required_profile_fields()
    {
        // Arrange
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "unit-user",
            Email = "unit@test.com",
            FirstName = "Unit",
            LastName = "Tester",
            PassPortNumber = "AB123456",
        };

        // Act
        _fixture.DbContext.Users.Add(user);
        await _fixture.DbContext.SaveChangesAsync();

        // Assert
        var persisted = await _fixture.DbContext.Users.FindAsync(user.Id);
        persisted.Should().NotBeNull();
        persisted!.FirstName.Should().Be("Unit");
        persisted.LastName.Should().Be("Tester");
        persisted.PassPortNumber.Should().Be("AB123456");
    }

    [Fact]
    public void new_user_should_start_at_version_zero()
    {
        var user = new User
        {
            FirstName = "A",
            LastName = "B",
            PassPortNumber = "C",
        };

        user.Version.Should().Be(0);
    }
}
