using Authentication.Application.Common.Interfaces;
using Authentication.Domain.Entities;
using FluentValidation;
using MediatR;

namespace Authentication.Application.Products.Commands;

public sealed record CreateProductCommand(string Name, string? Description, decimal Price, int StockQuantity)
    : IRequest<Guid>;

public sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
        RuleFor(x => x.StockQuantity).GreaterThanOrEqualTo(0);
    }
}

public sealed class CreateProductCommandHandler : IRequestHandler<CreateProductCommand, Guid>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly IEmpresaProvider _empresaProvider;
    private readonly ICurrentUserService _currentUser;

    public CreateProductCommandHandler(
        IApplicationDbContext context,
        ITenantProvider tenantProvider,
        IEmpresaProvider empresaProvider,
        ICurrentUserService currentUser)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _empresaProvider = empresaProvider;
        _currentUser = currentUser;
    }

    public async Task<Guid> Handle(CreateProductCommand request, CancellationToken cancellationToken)
    {
        var product = new Product
        {
            TenantId = _tenantProvider.TenantId,
            EmpresaId = _empresaProvider.EmpresaId,
            Name = request.Name,
            Description = request.Description,
            Price = request.Price,
            StockQuantity = request.StockQuantity,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedBy = _currentUser.UserId?.ToString()
        };

        _context.Products.Add(product);
        await _context.SaveChangesAsync(cancellationToken);

        return product.Id;
    }
}
