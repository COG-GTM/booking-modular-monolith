using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Web;

public interface ICurrentUserProvider
{
    long? GetCurrentUserId();
    Guid? GetCurrentUserGuid();
    bool IsInRole(string role);
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
        var nameIdentifier = _httpContextAccessor?.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);

        long.TryParse(nameIdentifier, out var userId);

        return userId;
    }

    public Guid? GetCurrentUserGuid()
    {
        var user = _httpContextAccessor?.HttpContext?.User;

        var subject = user?.FindFirstValue(JwtRegisteredClaimNames.Sub) ??
                      user?.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(subject, out var userId) ? userId : null;
    }

    public bool IsInRole(string role)
    {
        return _httpContextAccessor?.HttpContext?.User?.IsInRole(role) ?? false;
    }
}
