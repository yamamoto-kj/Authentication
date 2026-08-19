namespace Authentication.Domain.Entities;

/// <summary>Join entity: which Permissions a given TenantRole grants. Composite key, no surrogate Id.</summary>
public class TenantRolePermission
{
    public Guid TenantRoleId { get; set; }

    public Guid PermissionId { get; set; }
}
