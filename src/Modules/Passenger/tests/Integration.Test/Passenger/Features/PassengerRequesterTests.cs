using System.Security.Claims;
using FluentAssertions;
using Xunit;

namespace Integration.Test.Passenger.Features;

using global::Passenger.Passengers.Features.GettingPassengerById.V1;

public class PassengerRequesterTests
{
    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "test", "name", "role"));

    [Fact]
    public void should_read_user_id_from_sub_claim()
    {
        var userId = Guid.NewGuid();

        var requester = PassengerRequester.From(Principal(new Claim("sub", userId.ToString())));

        requester.UserId.Should().Be(userId);
        requester.IsAdmin.Should().BeFalse();
    }

    [Fact]
    public void should_fall_back_to_name_identifier_claim()
    {
        var userId = Guid.NewGuid();

        var requester = PassengerRequester.From(Principal(new Claim(ClaimTypes.NameIdentifier, userId.ToString())));

        requester.UserId.Should().Be(userId);
    }

    [Fact]
    public void should_detect_admin_role()
    {
        var requester = PassengerRequester.From(Principal(new Claim("role", "admin")));

        requester.IsAdmin.Should().BeTrue();
        requester.CanAccess(Guid.NewGuid()).Should().BeTrue();
        requester.CanAccess(null).Should().BeTrue();
    }

    [Fact]
    public void should_only_allow_owner_for_non_admin()
    {
        var userId = Guid.NewGuid();
        var requester = new PassengerRequester(userId, IsAdmin: false);

        requester.CanAccess(userId).Should().BeTrue();
        requester.CanAccess(Guid.NewGuid()).Should().BeFalse();
        requester.CanAccess(null).Should().BeFalse();
    }

    [Fact]
    public void should_deny_caller_without_user_id()
    {
        var requester = PassengerRequester.From(Principal(new Claim("sub", "not-a-guid")));

        requester.UserId.Should().BeNull();
        requester.CanAccess(null).Should().BeFalse();
        requester.CanAccess(Guid.NewGuid()).Should().BeFalse();
    }
}
