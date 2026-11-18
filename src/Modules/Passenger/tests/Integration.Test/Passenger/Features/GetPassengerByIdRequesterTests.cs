using System.Security.Claims;
using FluentAssertions;
using global::Passenger.Passengers.Features.GettingPassengerById.V1;
using Xunit;

namespace Integration.Test.Passenger.Features;

public class GetPassengerByIdRequesterTests
{
    private static ClaimsPrincipal User(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "test"));

    [Fact]
    public void should_take_requester_user_id_from_sub_claim()
    {
        var userId = Guid.NewGuid();
        var passengerId = Guid.NewGuid();

        var query = GetPassengerById.For(passengerId, User(new Claim("sub", userId.ToString())));

        query.Id.Should().Be(passengerId);
        query.RequesterUserId.Should().Be(userId);
        query.RequesterIsAdmin.Should().BeFalse();
    }

    [Fact]
    public void should_fall_back_to_name_identifier_claim_when_sub_is_missing()
    {
        var userId = Guid.NewGuid();

        var query = GetPassengerById.For(Guid.NewGuid(), User(new Claim(ClaimTypes.NameIdentifier, userId.ToString())));

        query.RequesterUserId.Should().Be(userId);
    }

    [Fact]
    public void should_prefer_sub_claim_over_name_identifier_claim()
    {
        var subId = Guid.NewGuid();

        var query = GetPassengerById.For(
            Guid.NewGuid(),
            User(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim("sub", subId.ToString()))
        );

        query.RequesterUserId.Should().Be(subId);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    public void should_have_no_requester_user_id_when_sub_is_not_a_guid(string sub)
    {
        var query = GetPassengerById.For(Guid.NewGuid(), User(new Claim("sub", sub)));

        query.RequesterUserId.Should().BeNull();
    }

    [Fact]
    public void should_have_no_requester_identity_for_anonymous_principal()
    {
        var query = GetPassengerById.For(Guid.NewGuid(), new ClaimsPrincipal(new ClaimsIdentity()));

        query.RequesterUserId.Should().BeNull();
        query.RequesterIsAdmin.Should().BeFalse();
    }

    [Fact]
    public void should_mark_requester_as_admin_when_in_admin_role()
    {
        var query = GetPassengerById.For(Guid.NewGuid(), User(new Claim(ClaimTypes.Role, "admin")));

        query.RequesterIsAdmin.Should().BeTrue();
        query.RequesterUserId.Should().BeNull();
    }

    [Fact]
    public void should_not_mark_requester_as_admin_for_user_role()
    {
        var query = GetPassengerById.For(
            Guid.NewGuid(),
            User(new Claim("sub", Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, "user"))
        );

        query.RequesterIsAdmin.Should().BeFalse();
    }

    [Fact]
    public void should_reject_empty_passenger_id()
    {
        var result = new GetPassengerByIdValidator().Validate(new GetPassengerById(Guid.Empty, Guid.NewGuid(), false));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(GetPassengerById.Id));
    }

    [Fact]
    public void should_accept_non_empty_passenger_id_without_requester()
    {
        var result = new GetPassengerByIdValidator().Validate(new GetPassengerById(Guid.NewGuid(), null, false));

        result.IsValid.Should().BeTrue();
    }
}
