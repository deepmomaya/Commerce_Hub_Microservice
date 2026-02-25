using CommerceHub.API.Models;

namespace CommerceHub.API.Repositories;

public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(string id);
    Task<Order> CreateAsync(Order order);
    Task<Order?> UpdateAsync(string id, Order order);
}

public interface IProductRepository
{
    Task<Product?> GetByIdAsync(string id);
    Task<bool> DecrementStockAsync(string id, int quantity);
    Task<bool> AdjustStockAsync(string id, int quantity);
}
