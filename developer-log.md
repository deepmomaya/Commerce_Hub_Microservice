# Developer Log — AI-Augmented Development

## Project: Commerce Hub Microservice

---

## AI Strategy

### Tools Used
- **Claude** — Primary code generation and architecture planning
- **Copilot** — In-editor suggestions and refactoring

### How I Provided Context to the AI

**Schema-First Prompting:**  
Before generating any code, I provided the AI with the full data schema upfront — including MongoDB document shapes for `Order` and `Product`, the required AMQP message envelope, and the four endpoint contracts. This prevented the AI from inventing its own naming conventions or field types mid-generation.

**Rule-Based Constraints:**  
I gave explicit rules at the start of each generation session:
- "All MongoDB write operations must use atomic filter+update patterns, never read-then-write"
- "Repositories must be injected as interfaces so they can be mocked in tests"
- "No logic in controllers — controllers only delegate to services"

**Layered Architecture Instruction:**  
I specified the exact folder layout (Controllers → Services → Repositories) before generating any file, so the AI would keep concerns separated without being corrected later.

---

## Human Audits — Three Key Corrections

### Correction 1: Atomic Stock Decrement Security

**AI's Initial Output:**
```csharp
// AI generated a read-then-write pattern:
var product = await _products.FindOneAsync(p => p.Id == id);
if (product.StockQuantity >= quantity)
{
    product.StockQuantity -= quantity;
    await _products.ReplaceOneAsync(p => p.Id == id, product);
    return true;
}
```

**Problem Identified:**  
This is a classic race condition. Under concurrent traffic, two threads could both read `StockQuantity = 5`, both pass the `>= 5` check, and both decrement — resulting in `-5` stock.

**My Correction:**  
Replaced with a single atomic MongoDB operation using a filter that includes the stock check:
```csharp
var filter = Builders<Product>.Filter.And(
    Builders<Product>.Filter.Eq(p => p.Id, id),
    Builders<Product>.Filter.Gte(p => p.StockQuantity, quantity)
);
var update = Builders<Product>.Update.Inc(p => p.StockQuantity, -quantity);
var result = await _products.UpdateOneAsync(filter, update);
return result.ModifiedCount > 0;
```
MongoDB executes this as a single atomic operation — no race condition possible.

---

### Correction 2: RabbitMQ Credential Security

**AI's Initial Output:**
```yaml
# docker-compose.yml
rabbitmq:
  environment:
    RABBITMQ_DEFAULT_USER: guest
    RABBITMQ_DEFAULT_PASS: guest
```

**Problem Identified:**  
Using the default `guest/guest` credentials is a security risk. RabbitMQ's default `guest` account is restricted to localhost connections by default — in a Docker network, this can cause authentication failures or expose default credentials in production-like environments.

**My Correction:**  
Changed to explicit non-default credentials (`admin/admin`) in docker-compose and ensured the API environment variables match:
```yaml
RABBITMQ_DEFAULT_USER: admin
RABBITMQ_DEFAULT_PASS: admin
```
And in the API service environment:
```yaml
RabbitMqSettings__UserName: admin
RabbitMqSettings__Password: admin
```

---

### Correction 3: Stock Rollback on Partial Failure

**AI's Initial Output:**  
The AI's checkout loop decremented stock for each item sequentially but had no rollback logic. If item 1 decremented successfully but item 2 failed (product not found or out of stock), item 1's stock would be permanently lost with no corresponding order created.

**Problem Identified:**  
Partial failure in a multi-product checkout left the system in an inconsistent state — money effectively taken from inventory without a corresponding order.

**My Correction:**  
Added a tracked list of successfully decremented items and a rollback helper:
```csharp
var decrementedProducts = new List<(string productId, int quantity)>();
// On any failure:
await RollbackStockAsync(decrementedProducts);

private async Task RollbackStockAsync(List<(string, int)> items)
{
    foreach (var (productId, quantity) in items)
        await _productRepository.AdjustStockAsync(productId, quantity); // re-add
}
```

---

## AI-Assisted Test Generation

### Process
I prompted the AI: *"Given this CheckoutAsync method signature and these validation rules, generate NUnit tests for every edge case that could cause a security or data integrity issue."*

The AI identified the following edge cases that I then reviewed and kept:

1. **Zero quantity** — AI correctly flagged that `quantity = 0` passes a `> 0` check only if the check is `> 0` not `>= 0`. Confirmed the service correctly uses `<= 0`.

2. **Negative unit price** — AI suggested this as a potential exploit (negative price could reduce total order amount). Added explicit validation.

3. **Empty customerId whitespace** — AI suggested checking `IsNullOrWhiteSpace` not just `IsNullOrEmpty`. This catches `" "` (space) as an invalid customer ID.

4. **Event not published on failure** — AI generated a test verifying `PublishAsync` is called `Times.Never` when checkout fails, which I verified against the rollback logic.

5. **Concurrent stock edge** — For the repository-level atomic test, AI suggested mocking `DecrementStockAsync` returning `false` to simulate the moment between a read returning sufficient stock and the atomic write failing — a scenario that can't be caught with integration tests easily.

---
