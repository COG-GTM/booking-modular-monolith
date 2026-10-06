using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BuildingBlocks.Web;

public interface ICurrentUserProvider
{
    long? GetCurrentUserId();
    string? GetCurrentUserIdentifier();
}

public class CurrentUserProvider : ICurrentUserProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public long? GetCurrentUserId()
    {
        return long.TryParse(GetCurrentUserIdentifier(), out var userId) ? userId : null;
    }

    // JwtBearer is configured with MapInboundClaims = false, so the subject stays in the raw "sub" claim.
    public string? GetCurrentUserIdentifier()
    {
        var user = _httpContextAccessor?.HttpContext?.User;

        var identifier = user?.FindFirstValue(JwtRegisteredClaimNames.Sub)
                         ?? user?.FindFirstValue(ClaimTypes.NameIdentifier);

        return string.IsNullOrWhiteSpace(identifier) ? null : identifier;
    }
}
