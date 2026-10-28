using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace BuildingBlocks.OpenTelemetryCollector;

public sealed class SuppressionAwarePropagator : TextMapPropagator
{
    private readonly TextMapPropagator _propagator = new CompositeTextMapPropagator(
        [new TraceContextPropagator(), new BaggagePropagator()]
    );

    public override ISet<string>? Fields => _propagator.Fields;

    public override void Inject<T>(PropagationContext context, T carrier, Action<T, string, string> setter)
    {
        if (Sdk.SuppressInstrumentation && Activity.Current?.OperationName == "System.Net.Http.HttpRequestOut")
            return;

        _propagator.Inject(context, carrier, setter);
    }

    public override PropagationContext Extract<T>(
        PropagationContext context,
        T carrier,
        Func<T, string, IEnumerable<string>?> getter
    ) => _propagator.Extract(context, carrier, getter);
}
