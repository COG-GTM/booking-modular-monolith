using Aspire.Hosting.ApplicationModel;
using Xunit;
using static AppHost.Unit.Test.AppHostModel;

namespace AppHost.Unit.Test;

// Image pins and arguments the AppHost needs for every container to start and report healthy.
public class InfrastructureContainerTests
{
    public static TheoryData<string?> Topologies => new() { null, MonolithTopology };

    [Theory]
    [MemberData(nameof(Topologies))]
    public async Task postgres_is_pinned_to_16_with_enough_prepared_transactions(string? topology)
    {
        await using var appHost = await BuildTopologyAsync(topology);
        var postgres = Assert.Single(GetResources(appHost.App).OfType<PostgresServerResource>());

        var image = Assert.Single(postgres.Annotations.OfType<ContainerImageAnnotation>());
        Assert.Equal("postgres", image.Image);
        Assert.Equal("16", image.Tag);

        var args = await GetArgumentsAsync(appHost, postgres);
        Assert.Contains("max_prepared_transactions=100", args);
        Assert.Contains("wal_level=logical", args);
        Assert.DoesNotContain("max_prepared_transactions=10", args);
    }

    [Theory]
    [MemberData(nameof(Topologies))]
    public async Task tempo_is_pinned_to_a_release_that_accepts_the_checked_in_config(string? topology)
    {
        await using var appHost = await BuildTopologyAsync(topology);
        var tempo = Assert.Single(GetResources(appHost.App).OfType<ContainerResource>(), r => r.Name == "tempo");

        var image = Assert.Single(tempo.Annotations.OfType<ContainerImageAnnotation>());
        Assert.Equal("grafana/tempo", image.Image);
        Assert.Equal("2.6.1", image.Tag);
        Assert.Contains("--config.file=/etc/tempo.yaml", await GetArgumentsAsync(appHost, tempo));
    }

    [Theory]
    [MemberData(nameof(Topologies))]
    public async Task elasticsearch_pulls_from_elastics_registry(string? topology)
    {
        await using var appHost = await BuildTopologyAsync(topology);
        var elasticsearch = Assert.Single(GetResources(appHost.App), r => r.Name == "elasticsearch");

        var image = Assert.Single(elasticsearch.Annotations.OfType<ContainerImageAnnotation>());
        Assert.Equal("docker.elastic.co", image.Registry);
        Assert.Equal("elasticsearch/elasticsearch", image.Image);
        Assert.Equal("8.17.0", image.Tag);
    }

    [Fact]
    public async Task shared_infrastructure_is_the_same_in_both_topologies()
    {
        await using var services = await BuildAsync();
        await using var monolith = await BuildMonolithAsync();

        var servicesContainers = ContainerNames(services);
        var monolithContainers = ContainerNames(monolith);

        Assert.Equal(servicesContainers, monolithContainers);
        Assert.Contains("postgres", servicesContainers);
        Assert.Contains("mongo", servicesContainers);
        Assert.Contains("rabbitmq", servicesContainers);
        Assert.Contains("eventstore", servicesContainers);
    }

    private static Task<AppHostUnderTest> BuildTopologyAsync(string? topology) =>
        topology is null ? BuildAsync() : BuildAsync(new() { ["AppHost:Topology"] = topology });

    private static List<string> ContainerNames(AppHostUnderTest appHost) =>
        GetResources(appHost.App)
            .Where(r => r.Annotations.OfType<ContainerImageAnnotation>().Any())
            .Select(r => r.Name)
            .Order()
            .ToList();

    private static async Task<List<string>> GetArgumentsAsync(AppHostUnderTest appHost, IResource resource)
    {
        var context = new CommandLineArgsCallbackContext([], resource);

        foreach (var annotation in resource.Annotations.OfType<CommandLineArgsCallbackAnnotation>())
            await annotation.Callback(context);

        return context.Args.Select(arg => arg.ToString()!).ToList();
    }
}
