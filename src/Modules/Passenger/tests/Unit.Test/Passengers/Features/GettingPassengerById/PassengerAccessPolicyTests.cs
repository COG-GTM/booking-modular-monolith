namespace Unit.Test.Passengers.Features.GettingPassengerById;

using System.Security.Claims;
using BuildingBlocks.Constants;
using FluentAssertions;
using global::Passenger.Passengers.Features.GettingPassengerById.V1;
using Xunit;

public class PassengerAccessPolicyTests
{
    private static readonly Guid OwnerUserId = Guid.CreateVersion7();

    [Fact]
    public void should_allow_admin_to_read_any_passenger()
    {
        var admin = CreatePrincipal(
            new Claim(ClaimTypes.Role, IdentityConstant.Role.Admin),
            new Claim("sub", Guid.CreateVersion7().ToString())
        );

        PassengerAccessPolicy.CanRead(admin, OwnerUserId).Should().BeTrue();
    }

    [Fact]
    public void should_allow_admin_to_read_legacy_passenger_without_owner()
    {
        var admin = CreatePrincipal(new Claim(ClaimTypes.Role, IdentityConstant.Role.Admin));

        PassengerAccessPolicy.CanRead(admin, Guid.Empty).Should().BeTrue();
    }

    [Fact]
    public void should_allow_owner_identified_by_sub_claim()
    {
        var owner = CreatePrincipal(
            new Claim(ClaimTypes.Role, IdentityConstant.Role.User),
            new Claim("sub", OwnerUserId.ToString())
        );

        PassengerAccessPolicy.CanRead(owner, OwnerUserId).Should().BeTrue();
    }

    [Fact]
    public void should_allow_owner_identified_by_name_identifier_claim()
    {
        var owner = CreatePrincipal(
            new Claim(ClaimTypes.Role, IdentityConstant.Role.User),
            new Claim(ClaimTypes.NameIdentifier, OwnerUserId.ToString())
        );

        PassengerAccessPolicy.CanRead(owner, OwnerUserId).Should().BeTrue();
    }

    [Fact]
    public void should_prefer_sub_claim_over_name_identifier_claim()
    {
        var user = CreatePrincipal(
            new Claim("sub", Guid.CreateVersion7().ToString()),
            new Claim(ClaimTypes.NameIdentifier, OwnerUserId.ToString())
        );

        PassengerAccessPolicy.CanRead(user, OwnerUserId).Should().BeFalse();
    }

    [Fact]
    public void should_forbid_non_admin_user_reading_another_users_passenger()
    {
        var otherUser = CreatePrincipal(
            new Claim(ClaimTypes.Role, IdentityConstant.Role.User),
            new Claim("sub", Guid.CreateVersion7().ToString())
        );

        PassengerAccessPolicy.CanRead(otherUser, OwnerUserId).Should().BeFalse();
    }

    [Fact]
    public void should_forbid_user_without_subject_claim()
    {
        var user = CreatePrincipal(
            new Claim(ClaimTypes.Role, IdentityConstant.Role.User),
            new Claim(ClaimTypes.Name, "user@sample.com")
        );

        PassengerAccessPolicy.CanRead(user, OwnerUserId).Should().BeFalse();
    }

    [Fact]
    public void should_forbid_user_with_non_guid_subject_claim()
    {
        var user = CreatePrincipal(new Claim("sub", "not-a-guid"));

        PassengerAccessPolicy.CanRead(user, OwnerUserId).Should().BeFalse();
    }

    [Fact]
    public void should_forbid_empty_subject_from_reading_legacy_passenger_without_owner()
    {
        var user = CreatePrincipal(new Claim("sub", Guid.Empty.ToString()));

        PassengerAccessPolicy.CanRead(user, Guid.Empty).Should().BeFalse();
    }

    [Fact]
    public void should_forbid_unauthenticated_principal()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        PassengerAccessPolicy.CanRead(anonymous, OwnerUserId).Should().BeFalse();
    }

    [Fact]
    public void should_not_treat_user_role_as_admin()
    {
        var user = CreatePrincipal(
            new Claim(ClaimTypes.Role, IdentityConstant.Role.User),
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim("sub", Guid.CreateVersion7().ToString())
        );

        PassengerAccessPolicy.CanRead(user, OwnerUserId).Should().BeFalse();
    }

    private static ClaimsPrincipal CreatePrincipal(params Claim[] claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "FakeBearer"));
    }
}
