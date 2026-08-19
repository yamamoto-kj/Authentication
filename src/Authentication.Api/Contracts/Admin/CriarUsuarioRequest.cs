namespace Authentication.Api.Contracts.Admin;

public sealed record CriarUsuarioRequest(
    string Cpf,
    string NomePrimeiro,
    string? NomeMeio,
    string NomeUltimo,
    string Email,
    VinculoInicialRequest? VinculoInicial);

/// <summary>
/// EmpresaIds is required and non-empty when present - see VinculoEmpresa's
/// doc comment: there is no implicit "all empresas" default, so granting a
/// membership without at least one explicit Empresa would leave the user
/// unable to access anything in the tenant.
/// </summary>
public sealed record VinculoInicialRequest(Guid TenantId, Guid TenantRoleId, IReadOnlyList<Guid> EmpresaIds);

public sealed record CriarUsuarioResponse(Guid UsuarioId, string Cpf, string SetPasswordToken);
