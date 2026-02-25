using CommerceHub.API.Events;
using CommerceHub.API.Infrastructure;
using CommerceHub.API.Models;
using CommerceHub.API.Repositories;
using CommerceHub.API.Services;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

namespace CommerceHub.Tests;

[TestFixture]
public class OrderServiceTests
{
    private Mock<IOrderRepository> _orderRepoMock = null!;
    private Mock<IProductRepository> _productRepoMock = null!;
    private Mock<IMessagePublisher> _publisherMock = null!;
    private IOptions<RabbitMqSettings> _rabbitOptions = null!;
    private OrderService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _orderRepoMock = new Mock<IOrderRepository>();
        _productRepoMock = new Mock<IProductRepository>();
        _publisherMock = new Mock<IMessagePublisher>();
        _rabbitOptions = Options.Create(new RabbitMqSettings { OrderCreatedQueue = "order.created" });

        _sut = new OrderService(
            _orderRepoMock.Object,
            _productRepoMock.Object,
            _publisherMock.Object,
            _rabbitOptions);
    }

    // ── Validation Tests ──────────────────────────────────────────────────────

    [Test]
    public async Task Checkout_WithNegativeQuantity_ReturnsError()
    {
        // Arrange
        var request = new CheckoutRequest
        {
            CustomerId = "cust-001",
            Items = new List<OrderItem>
            {
                new() { ProductId = "prod-001", Quantity = -1, UnitPrice = 10.00m }
            }
        };

        // Act
        var (order, error) = await _sut.CheckoutAsync(request);

        // Assert
        Assert.That(order, Is.Null);
        Assert.That(error, Does.Contain("greater than zero"));
    }

    [Test]
    public async Task Checkout_WithZeroQuantity_ReturnsError()
    {
        var request = new CheckoutRequest
        {
            CustomerId = "cust-001",
            Items = new List<OrderItem>
            {
                new() { ProductId = "prod-001", Quantity = 0, UnitPrice = 10.00m }
            }
        };

        var (order, error) = await _sut.CheckoutAsync(request);

        Assert.That(order, Is.Null);
        Assert.That(error, Does.Contain("greater than zero"));
    }

    [Test]
    public async Task Checkout_WithEmptyCustomerId_ReturnsError()
    {
        var request = new CheckoutRequest
        {
            CustomerId = "",
            Items = new List<OrderItem>
            {
                new() { ProductId = "prod-001", Quantity = 1, UnitPrice = 10.00m }
            }
        };

        var (order, error) = await _sut.CheckoutAsync(request);

        Assert.That(order, Is.Null);
        Assert.That(error, Does.Contain("CustomerId is required"));
    }

    [Test]
    public async Task Checkout_WithNoItems_ReturnsError()
    {
        var request = new CheckoutRequest
        {
            CustomerId = "cust-001",
            Items = new List<OrderItem>()
        };

        var (order, error) = await _sut.CheckoutAsync(request);

        Assert.That(order, Is.Null);
        Assert.That(error, Does.Contain("at least one item"));
    }

    [Test]
    public async Task Checkout_WithNegativeUnitPrice_ReturnsError()
    {
        var request = new CheckoutRequest
        {
            CustomerId = "cust-001",
            Items = new List<OrderItem>
            {
                new() { ProductId = "prod-001", Quantity = 1, UnitPrice = -5.00m }
            }
        };

        var (order, error) = await _sut.CheckoutAsync(request);

        Assert.That(order, Is.Null);
        Assert.That(error, Does.Contain("cannot be negative"));
    }

    // ── Stock Decrement Tests ──────────────────────────────────────────────────

    [Test]
    public async Task Checkout_WithSufficientStock_DecrementsStockAndCreatesOrder()
    {
        // Arrange
        var productId = "6507f1f77bcf86cd799439011"; // valid ObjectId format
        var product = new Product { Id = productId, Name = "Test", Price = 10m, StockQuantity = 10 };

        _productRepoMock.Setup(r => r.GetByIdAsync(productId)).ReturnsAsync(product);
        _productRepoMock.Setup(r => r.DecrementStockAsync(productId, 2)).ReturnsAsync(true);

        var expectedOrder = new Order
        {
            Id = "6507f1f77bcf86cd799439022",
            CustomerId = "cust-001",
            TotalAmount = 20.00m,
            Status = OrderStatus.Pending
        };
        _orderRepoMock.Setup(r => r.CreateAsync(It.IsAny<Order>())).ReturnsAsync(expectedOrder);
        _publisherMock.Setup(p => p.PublishAsync(It.IsAny<string>(), It.IsAny<OrderCreatedEvent>()))
            .Returns(Task.CompletedTask);

        var request = new CheckoutRequest
        {
            CustomerId = "cust-001",
            Items = new List<OrderItem>
            {
                new() { ProductId = productId, Quantity = 2, UnitPrice = 10.00m }
            }
        };

        // Act
        var (order, error) = await _sut.CheckoutAsync(request);

        // Assert
        Assert.That(error, Is.Null);
        Assert.That(order, Is.Not.Null);
        _productRepoMock.Verify(r => r.DecrementStockAsync(productId, 2), Times.Once);
        _orderRepoMock.Verify(r => r.CreateAsync(It.IsAny<Order>()), Times.Once);
    }

    [Test]
    public async Task Checkout_WithInsufficientStock_ReturnsErrorAndDoesNotCreateOrder()
    {
        // Arrange
        var productId = "6507f1f77bcf86cd799439011";
        var product = new Product { Id = productId, StockQuantity = 1 };

        _productRepoMock.Setup(r => r.GetByIdAsync(productId)).ReturnsAsync(product);
        // DecrementStock returns false = insufficient stock
        _productRepoMock.Setup(r => r.DecrementStockAsync(productId, 5)).ReturnsAsync(false);

        var request = new CheckoutRequest
        {
            CustomerId = "cust-001",
            Items = new List<OrderItem>
            {
                new() { ProductId = productId, Quantity = 5, UnitPrice = 10.00m }
            }
        };

        // Act
        var (order, error) = await _sut.CheckoutAsync(request);

        // Assert
        Assert.That(order, Is.Null);
        Assert.That(error, Does.Contain("Insufficient stock"));
        _orderRepoMock.Verify(r => r.CreateAsync(It.IsAny<Order>()), Times.Never);
    }

    [Test]
    public async Task Checkout_WhenProductNotFound_ReturnsError()
    {
        var productId = "6507f1f77bcf86cd799439011";
        _productRepoMock.Setup(r => r.GetByIdAsync(productId)).ReturnsAsync((Product?)null);

        var request = new CheckoutRequest
        {
            CustomerId = "cust-001",
            Items = new List<OrderItem>
            {
                new() { ProductId = productId, Quantity = 1, UnitPrice = 10.00m }
            }
        };

        var (order, error) = await _sut.CheckoutAsync(request);

        Assert.That(order, Is.Null);
        Assert.That(error, Does.Contain("not found"));
    }

    // ── Event Emission Tests ───────────────────────────────────────────────────

    [Test]
    public async Task Checkout_OnSuccess_PublishesOrderCreatedEvent()
    {
        // Arrange
        var productId = "6507f1f77bcf86cd799439011";
        var product = new Product { Id = productId, StockQuantity = 10 };

        _productRepoMock.Setup(r => r.GetByIdAsync(productId)).ReturnsAsync(product);
        _productRepoMock.Setup(r => r.DecrementStockAsync(productId, 1)).ReturnsAsync(true);

        var createdOrder = new Order
        {
            Id = "6507f1f77bcf86cd799439022",
            CustomerId = "cust-001",
            TotalAmount = 10m,
            Items = new List<OrderItem>
            {
                new() { ProductId = productId, Quantity = 1, UnitPrice = 10m }
            }
        };
        _orderRepoMock.Setup(r => r.CreateAsync(It.IsAny<Order>())).ReturnsAsync(createdOrder);

        OrderCreatedEvent? capturedEvent = null;
        _publisherMock
            .Setup(p => p.PublishAsync("order.created", It.IsAny<OrderCreatedEvent>()))
            .Callback<string, OrderCreatedEvent>((_, evt) => capturedEvent = evt)
            .Returns(Task.CompletedTask);

        var request = new CheckoutRequest
        {
            CustomerId = "cust-001",
            Items = new List<OrderItem>
            {
                new() { ProductId = productId, Quantity = 1, UnitPrice = 10m }
            }
        };

        // Act
        await _sut.CheckoutAsync(request);

        // Assert: event was published with correct data
        _publisherMock.Verify(p =>
            p.PublishAsync("order.created", It.IsAny<OrderCreatedEvent>()), Times.Once);

        Assert.That(capturedEvent, Is.Not.Null);
        Assert.That(capturedEvent!.OrderId, Is.EqualTo("6507f1f77bcf86cd799439022"));
        Assert.That(capturedEvent.CustomerId, Is.EqualTo("cust-001"));
    }

    [Test]
    public async Task Checkout_OnFailure_DoesNotPublishEvent()
    {
        // Arrange: insufficient stock scenario
        var productId = "6507f1f77bcf86cd799439011";
        var product = new Product { Id = productId, StockQuantity = 0 };

        _productRepoMock.Setup(r => r.GetByIdAsync(productId)).ReturnsAsync(product);
        _productRepoMock.Setup(r => r.DecrementStockAsync(productId, 1)).ReturnsAsync(false);

        var request = new CheckoutRequest
        {
            CustomerId = "cust-001",
            Items = new List<OrderItem>
            {
                new() { ProductId = productId, Quantity = 1, UnitPrice = 10m }
            }
        };

        // Act
        await _sut.CheckoutAsync(request);

        // Assert: no event published
        _publisherMock.Verify(p =>
            p.PublishAsync(It.IsAny<string>(), It.IsAny<OrderCreatedEvent>()), Times.Never);
    }

    // ── Update Order Tests ─────────────────────────────────────────────────────

    [Test]
    public async Task UpdateOrder_WhenStatusIsShipped_ReturnsError()
    {
        // Arrange
        var orderId = "6507f1f77bcf86cd799439011";
        var existingOrder = new Order { Id = orderId, Status = OrderStatus.Shipped };
        _orderRepoMock.Setup(r => r.GetByIdAsync(orderId)).ReturnsAsync(existingOrder);

        var request = new UpdateOrderRequest
        {
            CustomerId = "cust-001",
            Status = OrderStatus.Processing,
            TotalAmount = 100m
        };

        // Act
        var (order, error) = await _sut.UpdateOrderAsync(orderId, request);

        // Assert
        Assert.That(order, Is.Null);
        Assert.That(error, Does.Contain("already been shipped"));
        _orderRepoMock.Verify(r => r.UpdateAsync(It.IsAny<string>(), It.IsAny<Order>()), Times.Never);
    }

    [Test]
    public async Task UpdateOrder_WhenOrderNotFound_Returns404Error()
    {
        _orderRepoMock.Setup(r => r.GetByIdAsync("nonexistent")).ReturnsAsync((Order?)null);

        var (order, error) = await _sut.UpdateOrderAsync("nonexistent", new UpdateOrderRequest());

        Assert.That(order, Is.Null);
        Assert.That(error, Is.EqualTo("Order not found."));
    }
}
