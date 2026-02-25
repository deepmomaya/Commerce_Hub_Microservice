using CommerceHub.API.Models;
using CommerceHub.API.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace CommerceHub.API.Controllers;

[ApiController]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly IProductRepository _productRepository;
    private readonly ILogger<ProductsController> _logger;

    public ProductsController(IProductRepository productRepository, ILogger<ProductsController> logger)
    {
        _productRepository = productRepository;
        _logger = logger;
    }

    /// <summary>
    /// PATCH /api/products/{id}/stock
    /// Atomically adjusts product stock. Positive = restock, Negative = reduce.
    /// Blocks operations that would cause negative stock.
    /// </summary>
    [HttpPatch("{id}/stock")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AdjustStock(string id, [FromBody] StockAdjustmentRequest request)
    {
        var product = await _productRepository.GetByIdAsync(id);
        if (product == null)
            return NotFound(new { error = $"Product with id '{id}' not found." });

        var success = await _productRepository.AdjustStockAsync(id, request.Quantity);
        if (!success)
        {
            _logger.LogWarning("Stock adjustment failed for product {ProductId}: insufficient stock", id);
            return BadRequest(new { error = "Stock adjustment would result in negative stock level." });
        }

        // Return updated product
        var updated = await _productRepository.GetByIdAsync(id);
        return Ok(new
        {
            productId = id,
            previousStock = product.StockQuantity,
            adjustment = request.Quantity,
            newStock = updated!.StockQuantity
        });
    }
}
