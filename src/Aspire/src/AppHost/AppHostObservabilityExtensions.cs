using Aspire.Hosting.ApplicationModel;

static class AppHostObservabilityExtensions
{
    public static IResourceBuilder<ProjectResource> WithServiceObservability(
        this IResourceBuilder<ProjectResource> project,
        IResourceBuilder<ContainerResource> otelCollector
    ) =>
        project.WithEnvironment(context =>
        {
            context.EnvironmentVariables["OTEL_EXPORTER_OTLP_ENDPOINT"] = otelCollector.GetEndpoint("otel-grpc");
            context.EnvironmentVariables["OTEL_EXPORTER_OTLP_PROTOCOL"] = "grpc";
            context.EnvironmentVariables.Remove("OTEL_EXPORTER_OTLP_HEADERS");
        });
}
