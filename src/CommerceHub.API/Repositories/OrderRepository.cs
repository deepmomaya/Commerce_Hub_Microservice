using CommerceHub.API.Infrastructure;
using CommerceHub.API.Models;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace CommerceHub.API.Repositories;

public class OrderRepository : IOrderRepository
{
    private readonly IMongoCollection<Order> _orders;

    public OrderRepository(IOptions<MongoDbSettings> settings, IMongoClient mongoClient)
    {
        var db = mongoClient.GetDatabase(settings.Value.DatabaseName);
        _orders = db.GetCollection<Order>(settings.Value.OrdersCollection);
    }

    public async Task<Order?> GetByIdAsync(string id)
    {
        if (!ObjectId.TryParse(id, out _)) return null;
        return await _orders.Find(o => o.Id == id).FirstOrDefaultAsync();
    }

    public async Task<Order> CreateAsync(Order order)
    {
        await _orders.InsertOneAsync(order);
        return order;
    }

    public async Task<Order?> UpdateAsync(string id, Order order)
    {
        if (!ObjectId.TryParse(id, out _)) return null;

        order.Id = id;
        order.UpdatedAt = DateTime.UtcNow;

        var result = await _orders.ReplaceOneAsync(o => o.Id == id, order);
        return result.ModifiedCount > 0 ? order : null;
    }
}
