using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Web;

public interface ICurrentUserProvider
{
    Guid? GetCurrentUserId();
}

public class CurrentUserProvider : ICurrentUserProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }


    public Guid? GetCurrentUserId()
    {
        var subject = _httpContextAccessor?.HttpContext?.User?.GetUserIdClaim();

        return Guid.TryParse(subject, out var userId) ? userId : null;
    }
}

public static class UserClaimsExtensions
{
    // JwtBearer runs with MapInboundClaims = false, so tokens keep their short JWT claim names.
    public const string SubjectClaimType = "sub";
    public const string NameClaimType = "name";

    public static string? GetUserIdClaim(this ClaimsPrincipal? user) =>
        user?.FindFirstValue(SubjectClaimType) ?? user?.FindFirstValue(ClaimTypes.NameIdentifier);

    public static string? GetUserNameClaim(this ClaimsPrincipal? user) =>
        user?.FindFirstValue(NameClaimType) ?? user?.FindFirstValue(ClaimTypes.Name);
}
