namespace Authentication.Api.Contracts.Admin;

/// <summary>
/// Grants an existing Usuario (identified by CPF - it must already be
/// provisioned by a platform admin) a membership in the caller's own
/// current tenant. TenantId is never accepted from the client - it is
/// always the caller's own tenant_id claim, see VinculosController.
/// </summary>
public sealed record CriarVinculoRequest(string Cpf, Guid TenantRoleId, IReadOnlyList<Guid> EmpresaIds);

public sealed record CriarVinculoResponse(Guid VinculoId);
