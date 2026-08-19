namespace Authentication.Application.Products.Dtos;

public sealed record ProductDto(
    Guid Id,
    string Name,
    string? Description,
    decimal Price,
    int StockQuantity,
    DateTimeOffset CreatedAtUtc);
