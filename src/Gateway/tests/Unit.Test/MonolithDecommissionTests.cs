using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Gateway.Tests;

public class MonolithDecommissionTests
{
    [Theory]
    [InlineData("Booking")]
    [InlineData("Flight")]
    [InlineData("Passenger")]
    public void ServiceJwtDefaultsMatchTheLocalIdentityIssuerAndPreserveDockerOverrides(string module)
    {
        var servicesPath = Path.Combine(FindRepositoryRoot(), "src", "Services");
        var identity = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(servicesPath, "Identity.Host", "appsettings.json"))
            .Build();
        var settings = new ConfigurationBuilder().AddJsonFile(
            Path.Combine(servicesPath, $"{module}.Host", "appsettings.json")
        );
        Assert.Equal(identity["AuthOptions:IssuerUri"], settings.Build()["Jwt:Authority"]);
        var docker = settings
            .AddJsonFile(Path.Combine(servicesPath, $"{module}.Host", "appsettings.docker.json"))
            .Build();
        Assert.Equal("http://localhost:5000", docker["Jwt:Authority"]);
        Assert.Equal("http://identity-service:8080/.well-known/openid-configuration", docker["Jwt:MetadataAddress"]);
    }

    [Fact]
    public void SolutionAndProjectReferencesDoNotRequireTheRetiredHost()
    {
        var root = FindRepositoryRoot();
        var solution = File.ReadAllText(Path.Combine(root, "booking-modular-monolith.sln"));
        Assert.DoesNotContain("src\\Api\\", solution, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(root, "src", "Api")));

        foreach (var project in Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
        {
            var references = XDocument.Load(project).Descendants("ProjectReference");
            foreach (var reference in references)
            {
                var include = reference.Attribute("Include")!.Value.Replace('\\', '/');
                var target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, include));
                Assert.False(
                    target.StartsWith(
                        Path.Combine(root, "src", "Api") + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase
                    ),
                    $"{project} still references the retired host: {include}"
                );
            }
        }
    }

    [Theory]
    [InlineData("Booking", "Integration.Test")]
    [InlineData("Flight", "Integration.Test")]
    [InlineData("Flight", "EndToEnd.Test")]
    [InlineData("Identity", "Integration.Test")]
    [InlineData("Passenger", "Integration.Test")]
    public void ModuleTestsReferenceTheirOwnServiceHost(string module, string testProject)
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(root, "src", "Modules", module, "tests", testProject, $"{testProject}.csproj");
        var references = XDocument
            .Load(path)
            .Descendants("ProjectReference")
            .Select(reference =>
                Path.GetFullPath(
                    Path.Combine(Path.GetDirectoryName(path)!, reference.Attribute("Include")!.Value.Replace('\\', '/'))
                )
            );

        Assert.Contains(Path.Combine(root, "src", "Services", $"{module}.Host", $"{module}.Host.csproj"), references);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "booking-modular-monolith.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory.FullName;
    }
}
