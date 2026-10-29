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
        var userGuid =
            _httpContextAccessor?.HttpContext?.User?.FindFirstValue("sub")
            ?? _httpContextAccessor?.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(userGuid, out var parsedUserGuid) ? parsedUserGuid : null;
    }

    public bool IsInRole(string role)
    {
        return _httpContextAccessor?.HttpContext?.User?.IsInRole(role) ?? false;
    }
}
