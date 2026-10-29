using System.Security.Claims;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Http;
using Passenger.Passengers.Exceptions;
using Passenger.Passengers.Features.GettingPassengerById.V1;
using Xunit;

namespace Integration.Test;

public class PassengerOwnershipGuardTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid-subject")]
    [InlineData("a39c4a51-2079-4d84-b74e-e2349d251cf0")]
    public async Task should_deny_non_owners_before_accessing_the_database(string? subject)
    {
        var claims = new List<Claim> { new("role", "user") };
        if (subject is not null)
        {
            claims.Add(new Claim("sub", subject));
        }

        var provider = new CurrentUserProvider(
            new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) },
            }
        );
        var handler = new GetPassengerByIdHandler(provider, null!, null!);
        var query = new GetPassengerById(Guid.Parse("d66f03ad-30d2-47e0-919a-627461a6e6fb"));

        await Assert.ThrowsAsync<PassengerNotFoundException>(() => handler.Handle(query, CancellationToken.None));
    }
}
