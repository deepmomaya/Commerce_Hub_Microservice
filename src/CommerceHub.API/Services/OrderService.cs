using CommerceHub.API.Events;
using CommerceHub.API.Infrastructure;
using CommerceHub.API.Models;
using CommerceHub.API.Repositories;
using Microsoft.Extensions.Options;

namespace CommerceHub.API.Services;

public interface IOrderService
{
    Task<(Order? order, string? error)> CheckoutAsync(CheckoutRequest request);
    Task<Order?> GetOrderAsync(string id);
    Task<(Order? order, string? error)> UpdateOrderAsync(string id, UpdateOrderRequest request);
}

public class OrderService : IOrderService
{
    private readonly IOrderRepository _orderRepository;
    private readonly IProductRepository _productRepository;
    private readonly IMessagePublisher _messagePublisher;
    private readonly RabbitMqSettings _rabbitSettings;

    public OrderService(
        IOrderRepository orderRepository,
        IProductRepository productRepository,
        IMessagePublisher messagePublisher,
        IOptions<RabbitMqSettings> rabbitSettings)
    {
        _orderRepository = orderRepository;
        _productRepository = productRepository;
        _messagePublisher = messagePublisher;
        _rabbitSettings = rabbitSettings.Value;
    }

    public async Task<(Order? order, string? error)> CheckoutAsync(CheckoutRequest request)
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(request.CustomerId))
            return (null, "CustomerId is required.");

        if (request.Items == null || !request.Items.Any())
            return (null, "Order must contain at least one item.");

        foreach (var item in request.Items)
        {
            if (item.Quantity <= 0)
                return (null, $"Item quantity for product '{item.ProductId}' must be greater than zero.");

            if (item.UnitPrice < 0)
                return (null, $"Item price for product '{item.ProductId}' cannot be negative.");
        }

        // Verify stock and decrement atomically for each product
        var decrementedProducts = new List<(string productId, int quantity)>();
        try
        {
            foreach (var item in request.Items)
            {
                var product = await _productRepository.GetByIdAsync(item.ProductId);
                if (product == null)
                {
                    await RollbackStockAsync(decrementedProducts);
                    return (null, $"Product '{item.ProductId}' not found.");
                }

                var decremented = await _productRepository.DecrementStockAsync(item.ProductId, item.Quantity);
                if (!decremented)
                {
                    await RollbackStockAsync(decrementedProducts);
                    return (null, $"Insufficient stock for product '{item.ProductId}'.");
                }

                decrementedProducts.Add((item.ProductId, item.Quantity));
            }

            var order = new Order
            {
                CustomerId = request.CustomerId,
                Items = request.Items,
                Status = OrderStatus.Pending,
                TotalAmount = request.Items.Sum(i => i.Quantity * i.UnitPrice),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var created = await _orderRepository.CreateAsync(order);

            // Publish OrderCreated event
            var evt = new OrderCreatedEvent
            {
                OrderId = created.Id!,
                CustomerId = created.CustomerId,
                TotalAmount = created.TotalAmount,
                CreatedAt = created.CreatedAt,
                Items = created.Items.Select(i => new OrderCreatedItem
                {
                    ProductId = i.ProductId,
                    Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice
                }).ToList()
            };

            await _messagePublisher.PublishAsync(_rabbitSettings.OrderCreatedQueue, evt);

            return (created, null);
        }
        catch (Exception ex)
        {
            await RollbackStockAsync(decrementedProducts);
            return (null, $"An error occurred during checkout: {ex.Message}");
        }
    }

    public async Task<Order?> GetOrderAsync(string id)
    {
        return await _orderRepository.GetByIdAsync(id);
    }

    public async Task<(Order? order, string? error)> UpdateOrderAsync(string id, UpdateOrderRequest request)
    {
        var existing = await _orderRepository.GetByIdAsync(id);
        if (existing == null)
            return (null, "Order not found.");

        if (existing.Status == OrderStatus.Shipped)
            return (null, "Cannot update an order that has already been shipped.");

        var updated = new Order
        {
            Id = id,
            CustomerId = request.CustomerId,
            Items = request.Items,
            Status = request.Status,
            TotalAmount = request.TotalAmount,
            CreatedAt = existing.CreatedAt,
            UpdatedAt = DateTime.UtcNow
        };

        var result = await _orderRepository.UpdateAsync(id, updated);
        if (result == null)
            return (null, "Failed to update order.");

        return (result, null);
    }

    private async Task RollbackStockAsync(List<(string productId, int quantity)> decrementedProducts)
    {
        foreach (var (productId, quantity) in decrementedProducts)
        {
            await _productRepository.AdjustStockAsync(productId, quantity); // Add back
        }
    }
}
