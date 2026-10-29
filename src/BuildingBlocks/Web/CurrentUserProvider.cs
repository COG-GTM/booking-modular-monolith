using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Web;

public interface ICurrentUserProvider
{
    long? GetCurrentUserId();
    Guid? GetCurrentUserSubjectId();
    bool IsCurrentUserInRole(string role);
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

    public Guid? GetCurrentUserSubjectId()
    {
        var user = _httpContextAccessor?.HttpContext?.User;
        var subject = user?.FindFirstValue("sub") ?? user?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(subject, out var subjectId) ? subjectId : null;
    }

    public bool IsCurrentUserInRole(string role)
    {
        var user = _httpContextAccessor?.HttpContext?.User;
        return user is not null
            && (user.IsInRole(role) || user.HasClaim("role", role) || user.HasClaim(ClaimTypes.Role, role));
    }
}
