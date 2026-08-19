namespace Authentication.Domain.Exceptions;

public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message) { }
}

public sealed class TenantMismatchException : DomainException
{
    public TenantMismatchException()
        : base("The requested resource does not belong to the current tenant.") { }
}

public sealed class InsufficientStockException : DomainException
{
    public InsufficientStockException(Guid productId)
        : base($"Product '{productId}' does not have enough stock to satisfy the request.") { }
}
