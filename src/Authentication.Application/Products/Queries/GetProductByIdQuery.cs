using Authentication.Application.Common.Exceptions;
using Authentication.Application.Common.Interfaces;
using Authentication.Application.Products.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Authentication.Application.Products.Queries;

public sealed record GetProductByIdQuery(Guid Id) : IRequest<ProductDto>;

public sealed class GetProductByIdQueryHandler : IRequestHandler<GetProductByIdQuery, ProductDto>
{
    private readonly IApplicationDbContext _context;

    public GetProductByIdQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<ProductDto> Handle(GetProductByIdQuery request, CancellationToken cancellationToken)
    {
        // The global query filter on Products already restricts this to the
        // current tenant, so a match here is guaranteed to belong to the caller.
        var product = await _context.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

        if (product is null)
        {
            throw new NotFoundException(nameof(Domain.Entities.Product), request.Id);
        }

        return new ProductDto(product.Id, product.Name, product.Description, product.Price, product.StockQuantity, product.CreatedAtUtc);
    }
}
