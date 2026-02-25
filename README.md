# Commerce Hub Microservice

A production-ready .NET 8 microservice for order and inventory management, built with MongoDB, RabbitMQ, and Docker.

## Tech Stack

- **.NET** — Web API
- **MongoDB** — Persistent storage for Orders and Products
- **RabbitMQ** — Event messaging (OrderCreated events)
- **Docker** — One-command environment setup
- **NUnit** — Unit testing

---

## Running the Application (One Command)

```bash
docker-compose up --build
```

This starts:
| Service | URL |
|---|---|
| API + Swagger | http://localhost:8080/swagger |
| RabbitMQ Management UI | http://localhost:15672 (admin / admin) |
| MongoDB | mongodb://localhost:27017 |

To stop:
```bash
docker-compose down
```

To stop and remove all data:
```bash
docker-compose down -v
```

---

## API Endpoints

### POST /api/orders/checkout
Creates a new order. Verifies stock, atomically decrements inventory, and publishes an `OrderCreated` event.

**Request body:**
```json
{
  "customerId": "cust-001",
  "items": [
    {
      "productId": "<productId from MongoDB>",
      "quantity": 2,
      "unitPrice": 29.99
    }
  ]
}
```

### GET /api/orders/{id}
Returns a specific order. Returns 404 if not found.

### PUT /api/orders/{id}
Full replacement update. Returns 400 if the order is already `Shipped`.

**Request body:**
```json
{
  "customerId": "cust-001",
  "items": [...],
  "status": 1,
  "totalAmount": 59.98
}
```

Status values: `0=Pending, 1=Processing, 2=Shipped, 3=Delivered, 4=Cancelled`

### PATCH /api/products/{id}/stock
Atomically adjusts product stock. Use positive numbers to add stock, negative to reduce.

**Request body:**
```json
{ "quantity": -5 }
```

Returns 400 if adjustment would result in negative stock.

---

## Running Tests

From the project root:

```bash
cd src/CommerceHub.Tests
dotnet test
```

Or from the solution root:
```bash
dotnet test CommerceHub.sln
```

### Test Coverage
- ✅ Validation: negative quantities, zero quantities, missing customer ID, empty items
- ✅ Stock decrement: successful checkout, insufficient stock, product not found
- ✅ Event emission: event published on success, not published on failure
- ✅ Order updates: blocked when status is Shipped, 404 when not found

---

## Getting a Product ID for Testing

After running `docker-compose up`, connect to MongoDB to get seeded product IDs:

```bash
docker exec -it commercehub_mongo mongosh
use CommerceHub
db.Products.find({}, {_id: 1, name: 1})
```

Copy an `_id` value and use it in your checkout request.

---

## Project Structure

```
CommerceHub/
├── src/
│   ├── CommerceHub.API/
│   │   ├── Controllers/        # OrdersController, ProductsController
│   │   ├── Events/             # OrderCreatedEvent
│   │   ├── Infrastructure/     # MongoDbSettings, RabbitMqSettings
│   │   ├── Models/             # Order, Product, request models
│   │   ├── Repositories/       # IOrderRepository, IProductRepository + implementations
│   │   ├── Services/           # OrderService, RabbitMqPublisher
│   │   ├── Program.cs
│   │   └── appsettings.json
│   └── CommerceHub.Tests/
│       └── OrderServiceTests.cs
├── mongo-init/
│   └── init.js                 # Seeds sample products on first run
├── Dockerfile
├── docker-compose.yml
├── developer-log.md
└── README.md
```
