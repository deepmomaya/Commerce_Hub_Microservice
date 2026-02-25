using CommerceHub.API.Models;
using CommerceHub.API.Services;
using Microsoft.AspNetCore.Mvc;

namespace CommerceHub.API.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(IOrderService orderService, ILogger<OrdersController> logger)
    {
        _orderService = orderService;
        _logger = logger;
    }

    /// <summary>
    /// POST /api/orders/checkout
    /// Creates a new order, checks stock, decrements inventory, and publishes OrderCreated event.
    /// </summary>
    [HttpPost("checkout")]
    [ProducesResponseType(typeof(Order), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Checkout([FromBody] CheckoutRequest request)
    {
        var (order, error) = await _orderService.CheckoutAsync(request);
        if (error != null)
        {
            _logger.LogWarning("Checkout failed: {Error}", error);
            return BadRequest(new { error });
        }

        return CreatedAtAction(nameof(GetOrder), new { id = order!.Id }, order);
    }

    /// <summary>
    /// GET /api/orders/{id}
    /// Retrieves an order by ID.
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(Order), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrder(string id)
    {
        var order = await _orderService.GetOrderAsync(id);
        if (order == null)
            return NotFound(new { error = $"Order with id '{id}' not found." });

        return Ok(order);
    }

    /// <summary>
    /// PUT /api/orders/{id}
    /// Idempotent full replacement of an order. Blocked if status is Shipped.
    /// </summary>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(Order), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateOrder(string id, [FromBody] UpdateOrderRequest request)
    {
        var (order, error) = await _orderService.UpdateOrderAsync(id, request);

        if (error == "Order not found.")
            return NotFound(new { error });

        if (error != null)
            return BadRequest(new { error });

        return Ok(order);
    }
}
