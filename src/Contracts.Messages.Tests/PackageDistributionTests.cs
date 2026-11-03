using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core.Event;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Contracts.Messages.Tests;

public class PackageDistributionTests
{
    private const string PackageId = "BookingModularMonolith.Contracts.Messages";

    [Fact]
    public void Consumers_resolve_contracts_from_the_package_not_the_project()
    {
        var depsPath = Path.Combine(
            AppContext.BaseDirectory,
            $"{Assembly.GetExecutingAssembly().GetName().Name}.deps.json"
        );
        using var deps = JsonDocument.Parse(File.ReadAllText(depsPath));
        var libraries = deps.RootElement.GetProperty("libraries");
        var packageLibrary = libraries.GetProperty($"{PackageId}/1.0.0");

        Assert.Equal("package", packageLibrary.GetProperty("type").GetString());
        Assert.DoesNotContain(
            libraries.EnumerateObject(),
            library =>
                library.Name.StartsWith("Contracts.Messages/", StringComparison.Ordinal)
                && library.Value.GetProperty("type").GetString() == "project"
        );
    }

    [Fact]
    public void Packed_nupkg_has_exact_version_readme_and_only_lightweight_dependencies()
    {
        var repoRoot = FindRepoRoot();
        var version = XDocument
            .Load(Path.Combine(repoRoot, "Directory.Build.props"))
            .Descendants()
            .Single(element => element.Name.LocalName == "ContractsMessagesVersion")
            .Value;
        var packagePath = Path.Combine(repoRoot, "artifacts", "nuget", $"{PackageId}.{version}.nupkg");

        using var archive = ZipFile.OpenRead(packagePath);
        var nuspecEntry = Assert.Single(
            archive.Entries,
            entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal)
        );
        using var nuspecStream = nuspecEntry.Open();
        var nuspec = XDocument.Load(nuspecStream);
        var metadata = nuspec.Descendants().Single(element => element.Name.LocalName == "metadata");
        var dependencies = metadata.Elements().Single(element => element.Name.LocalName == "dependencies");
        var net10Group = dependencies
            .Elements()
            .Single(element =>
                element.Name.LocalName == "group" && (string?)element.Attribute("targetFramework") == "net10.0"
            );
        var dependencyIds = net10Group
            .Elements()
            .Where(element => element.Name.LocalName == "dependency")
            .Select(element => (string)element.Attribute("id")!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(PackageId, metadata.Elements().Single(element => element.Name.LocalName == "id").Value);
        Assert.Equal(version, metadata.Elements().Single(element => element.Name.LocalName == "version").Value);
        Assert.Equal("README.md", metadata.Elements().Single(element => element.Name.LocalName == "readme").Value);
        Assert.Contains(archive.Entries, entry => entry.FullName == "README.md");
        Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net10.0/Contracts.Messages.dll");
        var expectedDependencies = new HashSet<string>(
            ["MassTransit.Abstractions", "MediatR.Contracts"],
            StringComparer.Ordinal
        );
        Assert.True(expectedDependencies.SetEquals(dependencyIds));
    }

    [Fact]
    public void Assembly_version_is_stable_for_v1()
    {
        Assert.Equal(new Version(1, 0, 0, 0), typeof(UserCreated).Assembly.GetName().Version);
    }

    [Fact]
    public void Every_consumer_pins_the_exact_contracts_version()
    {
        var repoRoot = FindRepoRoot();
        var projects = Directory
            .EnumerateFiles(Path.Combine(repoRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(path =>
                !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Any(part => part is "bin" or "obj")
            )
            .ToArray();
        var consumingProjects = new HashSet<string>(StringComparer.Ordinal);

        foreach (var projectPath in projects)
        {
            var project = XDocument.Load(projectPath);
            var packageReferences = project
                .Descendants()
                .Where(element =>
                    element.Name.LocalName == "PackageReference" && (string?)element.Attribute("Include") == PackageId
                )
                .ToArray();

            foreach (var packageReference in packageReferences)
            {
                Assert.Equal("[$(ContractsMessagesVersion)]", (string?)packageReference.Attribute("Version"));
                consumingProjects.Add(Path.GetRelativePath(repoRoot, projectPath).Replace('\\', '/'));
            }

            Assert.DoesNotContain(
                project.Descendants().Where(element => element.Name.LocalName == "ProjectReference"),
                element =>
                    ((string?)element.Attribute("Include"))
                        ?.Replace('\\', '/')
                        .EndsWith("Contracts.Messages.csproj", StringComparison.OrdinalIgnoreCase) == true
            );
        }

        var expectedProjects = new[]
        {
            "src/BuildingBlocks/BuildingBlocks.csproj",
            "src/Modules/Booking/src/Booking.csproj",
            "src/Modules/Flight/src/Flight.csproj",
            "src/Modules/Identity/src/Identity.csproj",
            "src/Modules/Passenger/src/Passenger.csproj",
            "src/Modules/Booking/tests/Integration.Test/Integration.Test.csproj",
            "src/Modules/Flight/tests/Integration.Test/Integration.Test.csproj",
            "src/Modules/Identity/tests/Integration.Test/Integration.Test.csproj",
            "src/Modules/Passenger/tests/Integration.Test/Integration.Test.csproj",
            "src/Contracts.Messages.Tests/Contracts.Messages.Tests.csproj",
        };

        Assert.All(expectedProjects, project => Assert.Contains(project, consumingProjects));
    }

    [Fact]
    public void Nuget_config_maps_contracts_package_to_local_feed_only()
    {
        var nugetConfig = XDocument.Load(Path.Combine(FindRepoRoot(), "nuget.config"));
        var sources = nugetConfig.Descendants().Where(element => element.Name.LocalName == "add");
        var localFeed = Assert.Single(sources, element => (string?)element.Attribute("key") == "contracts-local");
        Assert.Equal("artifacts/nuget", (string?)localFeed.Attribute("value"));

        var localMapping = Assert.Single(
            nugetConfig.Descendants(),
            element =>
                element.Name.LocalName == "packageSource" && (string?)element.Attribute("key") == "contracts-local"
        );
        Assert.Contains(
            localMapping.Elements().Where(element => element.Name.LocalName == "package"),
            element => (string?)element.Attribute("pattern") == PackageId
        );

        var nugetOrgMapping = Assert.Single(
            nugetConfig.Descendants(),
            element => element.Name.LocalName == "packageSource" && (string?)element.Attribute("key") == "nuget.org"
        );
        Assert.DoesNotContain(
            nugetOrgMapping.Elements().Where(element => element.Name.LocalName == "package"),
            element => (string?)element.Attribute("pattern") == PackageId
        );
    }

    [Fact]
    public async Task Integration_event_marker_is_excluded_from_publish_topology()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMassTransit(configure => configure.UsingInMemory((_, _) => { }));
        await using var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<IBusControl>();

        await bus.StartAsync();
        try
        {
            Assert.True(bus.Topology.Publish<IIntegrationEvent>().Exclude);
            Assert.False(bus.Topology.Publish<UserCreated>().Exclude);
        }
        finally
        {
            await bus.StopAsync();
        }
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "booking-modular-monolith.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find booking-modular-monolith.sln.");
    }
}
