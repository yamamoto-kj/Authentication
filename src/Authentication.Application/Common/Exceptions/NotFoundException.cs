namespace Authentication.Application.Common.Exceptions;

public sealed class NotFoundException : Exception
{
    public NotFoundException(string entityName, object key)
        : base($"Entity \"{entityName}\" ({key}) was not found.") { }
}
