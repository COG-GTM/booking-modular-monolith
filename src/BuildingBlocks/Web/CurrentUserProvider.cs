using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Web;

public interface ICurrentUserProvider
{
    long? GetCurrentUserId();

    Guid? GetCurrentUserGuid();
}

public class CurrentUserProvider : ICurrentUserProvider
{
    private const string SubjectClaimType = "sub";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }


    public long? GetCurrentUserId()
    {
        return long.TryParse(GetSubject(), out var userId) ? userId : null;
    }

    public Guid? GetCurrentUserGuid()
    {
        return Guid.TryParse(GetSubject(), out var userId) && userId != Guid.Empty ? userId : null;
    }

    // JwtBearer is configured with MapInboundClaims = false, so the user id arrives as "sub".
    private string? GetSubject()
    {
        var user = _httpContextAccessor?.HttpContext?.User;

        return user?.FindFirstValue(SubjectClaimType) ?? user?.FindFirstValue(ClaimTypes.NameIdentifier);
    }
}
