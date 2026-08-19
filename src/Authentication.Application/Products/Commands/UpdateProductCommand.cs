using Authentication.Application.Common.Exceptions;
using Authentication.Application.Common.Interfaces;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Authentication.Application.Products.Commands;

/// <summary>
/// RowVersion must be echoed back by the client (from the ETag/DTO it last
/// read) so concurrent updates from parallel requests are detected via EF
/// Core optimistic concurrency instead of silently clobbering each other.
/// </summary>
public sealed record UpdateProductCommand(
    Guid Id,
    string Name,
    string? Description,
    decimal Price,
    int StockQuantity,
    byte[] RowVersion) : IRequest;

public sealed class UpdateProductCommandValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
        RuleFor(x => x.StockQuantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class UpdateProductCommandHandler : IRequestHandler<UpdateProductCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public UpdateProductCommandHandler(IApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task Handle(UpdateProductCommand request, CancellationToken cancellationToken)
    {
        var product = await _context.Products
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

        if (product is null)
        {
            throw new NotFoundException(nameof(Domain.Entities.Product), request.Id);
        }

        product.Name = request.Name;
        product.Description = request.Description;
        product.Price = request.Price;
        product.StockQuantity = request.StockQuantity;
        product.UpdatedAtUtc = DateTimeOffset.UtcNow;
        product.UpdatedBy = _currentUser.UserId?.ToString();

        _context.Entry(product).Property(p => p.RowVersion!).OriginalValue = request.RowVersion;

        // DbUpdateConcurrencyException bubbles up to the API's global
        // exception handler, which maps it to HTTP 409 Conflict.
        await _context.SaveChangesAsync(cancellationToken);
    }
}
