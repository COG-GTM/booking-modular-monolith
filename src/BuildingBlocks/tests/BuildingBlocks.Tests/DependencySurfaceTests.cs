using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace BuildingBlocks.Tests;

// Guards the dependency surface of the split BuildingBlocks.* libraries by inspecting the project graph.
public class DependencySurfaceTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static readonly string[] LibraryNames =
        Directory.GetFiles(Path.Combine(RepoRoot, "src", "BuildingBlocks"), "BuildingBlocks.*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(p => Path.GetFileNameWithoutExtension(p))
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static TheoryData<string> Libraries => new(LibraryNames);

    [Fact]
    public void every_buildingblocks_folder_is_its_own_library()
    {
        var expected = new[]
        {
            "Core", "EFCore", "Mongo", "EventStoreDB", "MassTransit", "PersistMessageProcessor", "Jwt", "Web",
            "Validation", "ProblemDetails", "HealthCheck", "OpenTelemetryCollector", "TestBase",
        };

        LibraryNames.Should().Contain(expected.Select(x => $"BuildingBlocks.{x}"));
    }

    [Theory]
    [MemberData(nameof(Libraries))]
    public void libraries_never_reference_the_aggregate(string library)
    {
        ProjectReferences(LibraryPath(library)).Should().NotContain(p => Path.GetFileName(p) == "BuildingBlocks.csproj");
    }

    [Fact]
    public void core_has_no_infrastructure_dependencies()
    {
        var core = LibraryPath("BuildingBlocks.Core");

        ProjectReferences(core).Should().BeEmpty();
        PackageReferences(core).Should().NotContain(p =>
            p.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ||
            p.StartsWith("Npgsql", StringComparison.Ordinal) ||
            p.StartsWith("MongoDB", StringComparison.Ordinal) ||
            p.StartsWith("EventStore", StringComparison.Ordinal) ||
            p == "MassTransit" || p == "MassTransit.RabbitMQ" ||
            p.StartsWith("Duende", StringComparison.Ordinal));
        FrameworkReferences(core).Should().NotContain("Microsoft.AspNetCore.App");
    }

    [Theory]
    [InlineData("BuildingBlocks.Web", "BuildingBlocks.EFCore")]
    [InlineData("BuildingBlocks.Web", "BuildingBlocks.PersistMessageProcessor")]
    [InlineData("BuildingBlocks.MassTransit", "BuildingBlocks.PersistMessageProcessor")]
    [InlineData("BuildingBlocks.MassTransit", "BuildingBlocks.EFCore")]
    [InlineData("BuildingBlocks.EFCore", "BuildingBlocks.PersistMessageProcessor")]
    [InlineData("BuildingBlocks.HealthCheck", "BuildingBlocks.EFCore")]
    [InlineData("BuildingBlocks.HealthCheck", "BuildingBlocks.MassTransit")]
    [InlineData("BuildingBlocks.ProblemDetails", "BuildingBlocks.EFCore")]
    [InlineData("BuildingBlocks.Mongo", "BuildingBlocks.EFCore")]
    [InlineData("BuildingBlocks.EventStoreDB", "BuildingBlocks.EFCore")]
    public void library_does_not_pull_in(string library, string forbidden)
    {
        ProjectClosure(LibraryPath(library)).Select(Path.GetFileNameWithoutExtension).Should().NotContain(forbidden);
    }

    [Fact]
    public void booking_module_builds_without_ef_core()
    {
        var booking = Path.Combine(RepoRoot, "src", "Modules", "Booking", "src", "Booking.csproj");
        var closure = ProjectClosure(booking).ToList();

        closure.Select(Path.GetFileNameWithoutExtension).Should()
            .NotContain(new[] { "BuildingBlocks", "BuildingBlocks.EFCore", "BuildingBlocks.PersistMessageProcessor" });
        closure.SelectMany(PackageReferences).Should()
            .NotContain(p => p.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
    }

    [Fact]
    public void booking_module_restore_graph_has_no_ef_core_packages()
    {
        var assets = Path.Combine(RepoRoot, "src", "Modules", "Booking", "src", "obj", "project.assets.json");
        if (!File.Exists(assets))
            return; // Not restored in this checkout; booking_module_builds_without_ef_core still guards the project graph.

        using var json = JsonDocument.Parse(File.ReadAllText(assets));
        json.RootElement.GetProperty("libraries").EnumerateObject().Select(x => x.Name).Should()
            .NotContain(name => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase));
    }

    private static string LibraryPath(string library) =>
        Directory.GetFiles(Path.Combine(RepoRoot, "src", "BuildingBlocks"), $"{library}.csproj", SearchOption.AllDirectories).Single();

    private static IEnumerable<string> ProjectReferences(string project) =>
        XDocument.Load(project).Descendants("ProjectReference")
            .Select(x => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!,
                x.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar))));

    private static IEnumerable<string> PackageReferences(string project) =>
        XDocument.Load(project).Descendants("PackageReference").Select(x => x.Attribute("Include")!.Value);

    private static IEnumerable<string> FrameworkReferences(string project) =>
        XDocument.Load(project).Descendants("FrameworkReference").Select(x => x.Attribute("Include")!.Value);

    private static IEnumerable<string> ProjectClosure(string project)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>(ProjectReferences(project));
        while (stack.Count > 0)
        {
            var next = stack.Pop();
            if (!seen.Add(next))
                continue;
            foreach (var reference in ProjectReferences(next))
                stack.Push(reference);
        }

        return seen;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "booking-modular-monolith.sln")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
