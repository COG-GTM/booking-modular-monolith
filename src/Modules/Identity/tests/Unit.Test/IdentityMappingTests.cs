using FluentAssertions;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

namespace Unit.Test;

using global::Identity.Identity.Features.RegisteringNewUser.V1;

[Collection(nameof(UnitTestFixture))]
public class IdentityMappingTests
{
    private readonly UnitTestFixture _fixture;

    public IdentityMappingTests(UnitTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void should_map_register_new_user_request_dto_to_command()
    {
        // Arrange
        var request = new FakeRegisterNewUserRequestDto().Generate();

        // Act
        var command = _fixture.Mapper.Map<RegisterNewUser>(request);

        // Assert
        command.Should().NotBeNull();
        command.FirstName.Should().Be(request.FirstName);
        command.LastName.Should().Be(request.LastName);
        command.Username.Should().Be(request.Username);
        command.Email.Should().Be(request.Email);
        command.Password.Should().Be(request.Password);
        command.ConfirmPassword.Should().Be(request.ConfirmPassword);
        command.PassportNumber.Should().Be(request.PassportNumber);
    }

    [Fact]
    public void should_map_register_new_user_result_to_response_dto()
    {
        // Arrange
        var result = new RegisterNewUserResult(Guid.NewGuid(), "Test", "User", "TestMyUser", "1234567890");

        // Act
        var response = _fixture.Mapper.Map<RegisterNewUserResponseDto>(result);

        // Assert
        response.Id.Should().Be(result.Id);
        response.FirstName.Should().Be(result.FirstName);
        response.LastName.Should().Be(result.LastName);
        response.Username.Should().Be(result.Username);
        response.PassportNumber.Should().Be(result.PassportNumber);
    }
}
