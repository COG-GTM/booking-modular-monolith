using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Passenger.Passengers.Exceptions;
using Passenger.Passengers.Features.GettingPassengerById.V1;
using Xunit;

namespace Integration.Test;

public class PassengerOwnershipGuardTests
{
    [Theory]
    [InlineData(null, true, "user")]
    [InlineData("", true, "user")]
    [InlineData("invalid", true, "user")]
    [InlineData("00000000-0000-0000-0000-000000000000", true, "user")]
    [InlineData("d66f03ad-30d2-47e0-919a-627461a6e6fb", true, "user")]
    [InlineData("d66f03ad-30d2-47e0-919a-627461a6e6fb", true, "admin")]
    [InlineData("a39c4a51-2079-4d84-b74e-e2349d251cf0", false, "user")]
    public async Task should_deny_invalid_or_non_owner_identity_before_reading_data(
        string? subject,
        bool authenticated,
        string role
    )
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, role) };
        if (subject is not null)
        {
            claims.Add(new Claim("sub", subject));
        }

        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticated ? "Test" : null)),
            },
        };
        var handler = new GetPassengerByIdHandler(null!, null!, accessor);

        await Assert.ThrowsAsync<PassengerNotFoundException>(
            () =>
                handler.Handle(
                    new GetPassengerById(Guid.Parse("a39c4a51-2079-4d84-b74e-e2349d251cf0")),
                    CancellationToken.None
                )
        );
    }

    [Fact]
    public async Task should_deny_when_no_http_context_exists()
    {
        var handler = new GetPassengerByIdHandler(null!, null!, new HttpContextAccessor());
        await Assert.ThrowsAsync<PassengerNotFoundException>(
            () => handler.Handle(new GetPassengerById(Guid.NewGuid()), CancellationToken.None)
        );
    }
}
