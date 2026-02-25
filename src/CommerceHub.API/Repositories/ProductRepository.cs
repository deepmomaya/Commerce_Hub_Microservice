using CommerceHub.API.Infrastructure;
using CommerceHub.API.Models;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace CommerceHub.API.Repositories;

public class ProductRepository : IProductRepository
{
    private readonly IMongoCollection<Product> _products;

    public ProductRepository(IOptions<MongoDbSettings> settings, IMongoClient mongoClient)
    {
        var db = mongoClient.GetDatabase(settings.Value.DatabaseName);
        _products = db.GetCollection<Product>(settings.Value.ProductsCollection);
    }

    public async Task<Product?> GetByIdAsync(string id)
    {
        if (!ObjectId.TryParse(id, out _)) return null;
        return await _products.Find(p => p.Id == id).FirstOrDefaultAsync();
    }

    /// <summary>
    /// Atomically decrements stock ONLY if sufficient quantity exists.
    /// Uses a filter that checks stockQuantity >= quantity to prevent negative stock.
    /// This is a single atomic MongoDB operation — race-condition safe.
    /// </summary>
    public async Task<bool> DecrementStockAsync(string id, int quantity)
    {
        if (!ObjectId.TryParse(id, out _)) return false;

        // Atomic: only decrement if current stock >= requested quantity
        var filter = Builders<Product>.Filter.And(
            Builders<Product>.Filter.Eq(p => p.Id, id),
            Builders<Product>.Filter.Gte(p => p.StockQuantity, quantity)
        );

        var update = Builders<Product>.Update.Inc(p => p.StockQuantity, -quantity);

        var result = await _products.UpdateOneAsync(filter, update);
        return result.ModifiedCount > 0;
    }

    /// <summary>
    /// Atomically adjusts stock. Positive = add stock, Negative = remove stock.
    /// Blocks negative adjustments that would push stock below 0.
    /// </summary>
    public async Task<bool> AdjustStockAsync(string id, int quantity)
    {
        if (!ObjectId.TryParse(id, out _)) return false;

        FilterDefinition<Product> filter;

        if (quantity < 0)
        {
            // For decrements, guard against going negative
            filter = Builders<Product>.Filter.And(
                Builders<Product>.Filter.Eq(p => p.Id, id),
                Builders<Product>.Filter.Gte(p => p.StockQuantity, Math.Abs(quantity))
            );
        }
        else
        {
            filter = Builders<Product>.Filter.Eq(p => p.Id, id);
        }

        var update = Builders<Product>.Update.Inc(p => p.StockQuantity, quantity);
        var result = await _products.UpdateOneAsync(filter, update);
        return result.ModifiedCount > 0;
    }
}
