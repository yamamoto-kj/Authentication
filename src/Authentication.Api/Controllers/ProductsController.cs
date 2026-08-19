using Authentication.Api.Extensions;
using Authentication.Application.Products.Commands;
using Authentication.Application.Products.Dtos;
using Authentication.Application.Products.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Authentication.Api.Controllers;

/// <summary>
/// Sample tenant-scoped resource. Every action is implicitly authenticated
/// (global fallback policy) and additionally requires the "RequireTenant"
/// policy, so a token without a tenant_id claim (which should never happen,
/// but defense-in-depth) is rejected before reaching the handler.
/// </summary>
[ApiController]
[Route("api/v1/products")]
[Authorize(Policy = "RequireTenant")]
[EnableRateLimiting(RateLimitingExtensions.TenantPolicy)]
[Produces("application/json")]
public class ProductsController : ControllerBase
{
    private readonly ISender _mediator;

    public ProductsController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ProductDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ProductDto>>> List(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetProductsQuery(page, pageSize), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetProductByIdQuery(id), cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> Create([FromBody] CreateProductCommand command, CancellationToken cancellationToken)
    {
        var id = await _mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id }, new { id });
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Update(Guid id, [FromBody] UpdateProductRequest request, CancellationToken cancellationToken)
    {
        await _mediator.Send(
            new UpdateProductCommand(id, request.Name, request.Description, request.Price, request.StockQuantity, request.RowVersion),
            cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteProductCommand(id), cancellationToken);
        return NoContent();
    }
}

public sealed record UpdateProductRequest(string Name, string? Description, decimal Price, int StockQuantity, byte[] RowVersion);
