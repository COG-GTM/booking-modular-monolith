namespace Unit.Test.Identity.Features;

using FluentAssertions;
using global::Identity.Identity.Features;
using global::Identity.Identity.Features.RegisteringNewUser.V1;
using Mapster;
using MapsterMapper;
using Unit.Test.Fakes;
using Xunit;

public class IdentityMappingsTests
{
    private readonly TypeAdapterConfig _config = new();
    private readonly IMapper _mapper;

    public IdentityMappingsTests()
    {
        new IdentityMappings().Register(_config);
        _mapper = new Mapper(_config);
    }

    [Fact]
    public void should_map_register_new_user_request_dto_to_register_new_user()
    {
        var dto = new FakeRegisterNewUserRequestDto().Generate();

        var command = _mapper.Map<RegisterNewUser>(dto);

        command.FirstName.Should().Be(dto.FirstName);
        command.LastName.Should().Be(dto.LastName);
        command.Username.Should().Be(dto.Username);
        command.Email.Should().Be(dto.Email);
        command.Password.Should().Be(dto.Password);
        command.ConfirmPassword.Should().Be(dto.ConfirmPassword);
        command.PassportNumber.Should().Be(dto.PassportNumber);
    }

    [Fact]
    public void should_map_register_new_user_result_to_response_dto()
    {
        var result = new RegisterNewUserResult(Guid.NewGuid(), "First", "Last", "username", "P123");

        var dto = result.Adapt<RegisterNewUserResponseDto>(_config);

        dto.Id.Should().Be(result.Id);
        dto.FirstName.Should().Be(result.FirstName);
        dto.LastName.Should().Be(result.LastName);
        dto.Username.Should().Be(result.Username);
        dto.PassportNumber.Should().Be(result.PassportNumber);
    }
}
