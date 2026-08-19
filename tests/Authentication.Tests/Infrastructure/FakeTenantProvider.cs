using Authentication.Application.Common.Interfaces;

namespace Authentication.Tests.Infrastructure;

public sealed class FakeTenantProvider : ITenantProvider
{
    public FakeTenantProvider(Guid tenantId) => TenantId = tenantId;

    public Guid TenantId { get; }

    public bool IsResolved => true;
}

public sealed class FakeEmpresaProvider : IEmpresaProvider
{
    public FakeEmpresaProvider(Guid empresaId) => EmpresaId = empresaId;

    public Guid EmpresaId { get; }

    public bool IsResolved => true;
}

public sealed class FakeCurrentUserService : ICurrentUserService
{
    public Guid? UserId => Guid.Empty;
    public string? UserName => "test-user";
    public Guid? TenantId => Guid.Empty;
    public Guid? EmpresaId => Guid.Empty;
    public bool IsInRole(string role) => false;
}
