namespace Authentication.Api.Contracts.Admin;

public sealed record ContextoDisponivel(
    Guid TenantId,
    string TenantNome,
    IReadOnlyList<EmpresaDisponivel> Empresas);

public sealed record EmpresaDisponivel(Guid EmpresaId, string RazaoSocial);
