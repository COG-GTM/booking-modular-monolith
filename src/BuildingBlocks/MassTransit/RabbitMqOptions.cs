namespace BuildingBlocks.MassTransit;

public class RabbitMqOptions
{
    public string HostName { get; set; }
    public string ExchangeName { get; set; }
    public string UserName { get; set; }
    public string Password { get; set; }
    public ushort? Port { get; set; }

    /// <summary>
    /// Prefix applied to every receive-endpoint (queue) name so that each service hosting the same consumer type
    /// gets its own queue on the shared broker. Falls back to the kebab-cased <c>AppOptions:Name</c>.
    /// </summary>
    public string? QueuePrefix { get; set; }
}
