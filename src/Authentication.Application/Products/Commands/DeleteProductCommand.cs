using Authentication.Application.Common.Exceptions;
using Authentication.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Authentication.Application.Products.Commands;

public sealed record DeleteProductCommand(Guid Id) : IRequest;

public sealed class DeleteProductCommandHandler : IRequestHandler<DeleteProductCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public DeleteProductCommandHandler(IApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task Handle(DeleteProductCommand request, CancellationToken cancellationToken)
    {
        var product = await _context.Products
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

        if (product is null)
        {
            throw new NotFoundException(nameof(Domain.Entities.Product), request.Id);
        }

        // Soft delete: keeps audit history and lets a SaveChangesInterceptor
        // enforce it uniformly instead of every handler issuing hard deletes.
        product.IsDeleted = true;
        product.DeletedAtUtc = DateTimeOffset.UtcNow;
        product.UpdatedBy = _currentUser.UserId?.ToString();

        await _context.SaveChangesAsync(cancellationToken);
    }
}
