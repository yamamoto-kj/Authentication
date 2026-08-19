using Authentication.Application.Common.Interfaces;
using Authentication.Application.Products.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Authentication.Application.Products.Queries;

/// <summary>Paged listing, scoped to the current tenant by the DbContext's global query filter.</summary>
public sealed record GetProductsQuery(int Page = 1, int PageSize = 20) : IRequest<PagedResult<ProductDto>>;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public sealed class GetProductsQueryHandler : IRequestHandler<GetProductsQuery, PagedResult<ProductDto>>
{
    private readonly IApplicationDbContext _context;

    public GetProductsQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<PagedResult<ProductDto>> Handle(GetProductsQuery request, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var query = _context.Products
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAtUtc);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new ProductDto(p.Id, p.Name, p.Description, p.Price, p.StockQuantity, p.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return new PagedResult<ProductDto>(items, page, pageSize, totalCount);
    }
}
