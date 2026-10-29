namespace BuildingBlocks.HealthCheck;

// Mirrors BuildingBlocks.MassTransit ConfigureRabbitMqHost so the probe hits the broker the bus actually uses:
// ConnectionStrings:rabbitmq (Aspire) wins, otherwise RabbitMqOptions host/port/virtual host/credentials.
public static class RabbitMqHealthUri
{
    private const int DefaultPort = 5672;

    public static Uri Resolve(
        string? connectionString,
        string? hostName,
        ushort? port,
        string? virtualHost,
        string? userName,
        string? password)
    {
        if (!string.IsNullOrEmpty(connectionString))
            return new Uri(connectionString);

        var builder = new UriBuilder("amqp", hostName, port ?? DefaultPort)
        {
            UserName = Uri.EscapeDataString(userName ?? string.Empty),
            Password = Uri.EscapeDataString(password ?? string.Empty),
        };

        if (!string.IsNullOrWhiteSpace(virtualHost) && virtualHost != "/")
            builder.Path = Uri.EscapeDataString(virtualHost);

        return builder.Uri;
    }
}
