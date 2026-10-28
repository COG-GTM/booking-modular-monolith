namespace BuildingBlocks.MassTransit;

public class RabbitMqOptions
{
    public string HostName { get; set; } = "localhost";
    public string VirtualHost { get; set; } = "/";
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public ushort Port { get; set; } = 5672;
}
