namespace Authentication.Domain.Common;

/// <summary>
/// Base type for every persisted entity. RowVersion backs optimistic concurrency
/// so concurrent writes under multi-request load fail fast with a conflict
/// instead of silently overwriting each other (lost-update prevention).
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public string? UpdatedBy { get; set; }

    [System.ComponentModel.DataAnnotations.Timestamp]
    public byte[]? RowVersion { get; set; }

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAtUtc { get; set; }
}
