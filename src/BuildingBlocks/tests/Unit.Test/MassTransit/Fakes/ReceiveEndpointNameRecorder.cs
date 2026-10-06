namespace Unit.Test.MassTransit.Fakes;

using global::MassTransit;

public class ReceiveEndpointNameRecorder : IConfigureReceiveEndpoint
{
    public List<string> Names { get; } = new();

    public void Configure(string name, IReceiveEndpointConfigurator configurator)
    {
        Names.Add(name);
    }
}
