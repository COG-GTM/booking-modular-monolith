using Aspire.Hosting.ApplicationModel;

static class AppHostObservabilityExtensions
{
    public static IResourceBuilder<ProjectResource> WithServiceObservability(
        this IResourceBuilder<ProjectResource> project,
        IResourceBuilder<ContainerResource> otelCollector
    ) =>
        project
            .WithEnvironment("ObservabilityOptions__ServiceName", project.Resource.Name)
            .WithEnvironment("ObservabilityOptions__UseOTLPExporter", "true")
            .WithEnvironment(
                "ObservabilityOptions__OTLPOptions__OTLPGrpcExporterEndpoint",
                otelCollector.GetEndpoint("otel-grpc"))
            .WithEnvironment("ObservabilityOptions__UseAspireOTLPExporter", "true")
            .WithEnvironment(context =>
            {
                if (context.EnvironmentVariables.TryGetValue("OTEL_EXPORTER_OTLP_ENDPOINT", out var endpoint))
                {
                    context.EnvironmentVariables[
                        "ObservabilityOptions__AspireDashboardOTLPOptions__OTLPGrpcExporterEndpoint"
                    ] = endpoint;
                }
            });
}
