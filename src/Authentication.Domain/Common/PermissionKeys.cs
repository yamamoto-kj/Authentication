namespace Authentication.Domain.Common;

/// <summary>
/// The fixed, system-wide catalog of actions the API can authorize (see
/// Permission/TenantRolePermission). Each Tenant decides which of these its
/// own TenantRoles grant; this class exists so the seeded/admin-configured
/// Permission.Chave values and the policy checks that gate endpoints never
/// drift apart into two hand-typed copies of the same string.
/// </summary>
public static class PermissionKeys
{
    public const string ProdutosGerenciar = "produtos:gerenciar";
    public const string UsuariosConvidar = "usuarios:convidar";
}
