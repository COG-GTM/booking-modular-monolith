using MassTransit;

namespace BuildingBlocks.MassTransit;

// Queue names are owned by the module that declares the consumer ("passenger.register-new-user-handler"),
// so the monolith and a standalone service host bind the same queue (competing consumers) instead of
// each host getting its own copy of every message.
public sealed class ModuleEndpointNameFormatter : KebabCaseEndpointNameFormatter
{
    public const char Separator = '.';

    public static readonly ModuleEndpointNameFormatter Instance = new();

    private ModuleEndpointNameFormatter()
        : base(prefix: null, includeNamespace: false) { }

    public override string Consumer<T>() => Qualify(typeof(T), base.Consumer<T>());

    public override string Saga<T>() => Qualify(typeof(T), base.Saga<T>());

    public override string ExecuteActivity<T, TArguments>() =>
        Qualify(typeof(T), base.ExecuteActivity<T, TArguments>());

    public override string CompensateActivity<T, TLog>() => Qualify(typeof(T), base.CompensateActivity<T, TLog>());

    public static string ModuleName(Type type) =>
        (type.Assembly.GetName().Name ?? type.Name).Replace('.', '-').ToLowerInvariant();

    private static string Qualify(Type type, string endpointName) => $"{ModuleName(type)}{Separator}{endpointName}";
}
