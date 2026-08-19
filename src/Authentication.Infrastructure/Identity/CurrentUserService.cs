using System.Security.Claims;
using Authentication.Application.Common.Interfaces;
using Authentication.Infrastructure.MultiTenancy;
using Microsoft.AspNetCore.Http;

namespace Authentication.Infrastructure.Identity;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    public Guid? UserId
    {
        get
        {
            var value = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                         ?? User?.FindFirst("sub")?.Value;
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public string? UserName => User?.Identity?.Name ?? User?.FindFirst(ClaimTypes.Name)?.Value;

    public Guid? TenantId
    {
        get
        {
            var value = User?.FindFirst(TenantClaimTypes.TenantId)?.Value;
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public Guid? EmpresaId
    {
        get
        {
            var value = User?.FindFirst(TenantClaimTypes.EmpresaId)?.Value;
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public bool IsInRole(string role) => User?.IsInRole(role) ?? false;
}
