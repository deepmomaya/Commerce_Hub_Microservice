namespace CommerceHub.API.Infrastructure;

public class MongoDbSettings
{
    public string ConnectionString { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public string OrdersCollection { get; set; } = "Orders";
    public string ProductsCollection { get; set; } = "Products";
}

public class RabbitMqSettings
{
    public string HostName { get; set; } = string.Empty;
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string OrderCreatedQueue { get; set; } = "order.created";
}
