namespace Authentication.Application.Common.Interfaces;

public interface ICurrentUserService
{
    Guid? UserId { get; }

    string? UserName { get; }

    Guid? TenantId { get; }

    Guid? EmpresaId { get; }

    bool IsInRole(string role);
}
