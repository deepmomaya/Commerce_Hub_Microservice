// Seed the Products collection with sample data
db = db.getSiblingDB('CommerceHub');

db.Products.insertMany([
  {
    name: "Laptop Pro 15",
    price: NumberDecimal("1299.99"),
    stockQuantity: 50,
    createdAt: new Date()
  },
  {
    name: "Wireless Mouse",
    price: NumberDecimal("29.99"),
    stockQuantity: 200,
    createdAt: new Date()
  },
  {
    name: "USB-C Hub",
    price: NumberDecimal("49.99"),
    stockQuantity: 150,
    createdAt: new Date()
  }
]);

print("Seeded Products collection with 3 sample products.");
