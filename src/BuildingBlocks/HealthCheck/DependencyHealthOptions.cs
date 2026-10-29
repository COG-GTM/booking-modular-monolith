namespace BuildingBlocks.HealthCheck;

internal sealed class ConnectionStringOptions
{
    public string? ConnectionString { get; set; }
}

internal sealed class RabbitMqHealthOptions
{
    public string? HostName { get; set; }
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public int? Port { get; set; }
    public string? VirtualHost { get; set; } = "/";
}
