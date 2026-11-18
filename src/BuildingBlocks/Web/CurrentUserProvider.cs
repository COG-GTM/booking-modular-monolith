using System.Security.Claims;
using BuildingBlocks.Constants;
using BuildingBlocks.Exception;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Web;

public interface ICurrentUserProvider
{
    long? GetCurrentUserId();
    Guid? GetCurrentUserSubjectId();
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

    public Guid? GetCurrentUserSubjectId()
    {
        var user = _httpContextAccessor?.HttpContext?.User;

        var subject = user?.FindFirstValue("sub") ?? user?.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(subject, out var subjectId) ? subjectId : null;
    }

    public bool IsInRole(string role)
    {
        return _httpContextAccessor?.HttpContext?.User?.IsInRole(role) ?? false;
    }
}

public static class CurrentUserProviderExtensions
{
    public static void EnsureOwnerOrAdmin(this ICurrentUserProvider currentUserProvider, Guid? ownerUserId)
    {
        if (currentUserProvider.IsInRole(IdentityConstant.Role.Admin))
        {
            return;
        }

        var subjectId = currentUserProvider.GetCurrentUserSubjectId();

        if (subjectId is null || ownerUserId is null || subjectId != ownerUserId)
        {
            throw new ForbiddenException("You are not allowed to access this resource.");
        }
    }
}
