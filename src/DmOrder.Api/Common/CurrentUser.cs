using System.Security.Claims;
using DmOrder.Application.Common.Interfaces;

namespace DmOrder.Api.Common;

/// <summary>Reads the caller's identity from the current request's claims principal.</summary>
public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public Guid? UserId =>
        Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public string? Email => Principal?.FindFirstValue(ClaimTypes.Email);

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public bool IsInRole(string role) => Principal?.IsInRole(role) ?? false;

    public Guid RequireUserId() =>
        UserId ?? throw new UnauthorizedAccessException("The request is not authenticated.");
}
