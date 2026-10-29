using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Host.Test;

public class FlightHostConfigurationTests
{
    [Fact]
    public void local_and_docker_settings_keep_host_endpoints_and_flight_storage_aligned()
    {
        var root = FindRepositoryRoot();
        var local = LoadConfiguration(root, docker: false);
        var docker = LoadConfiguration(root, docker: true);

        AssertKestrel(local, "http://localhost:5102", "http://localhost:5103");
        AssertKestrel(docker, "http://+:80", "http://+:81");
        AssertPostgres(local, "localhost");
        AssertPostgres(docker, "postgres");
        AssertMongo(local);
        AssertMongo(docker);

        BuildingBlocks
            .MassTransit.Extensions.ResolveTransport(local)
            .Should()
            .Be(BuildingBlocks.MassTransit.TransportType.RabbitMq);
        BuildingBlocks
            .MassTransit.Extensions.ResolveTransport(docker)
            .Should()
            .Be(BuildingBlocks.MassTransit.TransportType.RabbitMq);
        docker["RabbitMqOptions:HostName"].Should().Be("rabbitmq");

        local.GetValue<bool>("HealthOptions:Enabled").Should().BeFalse();
        docker.GetValue<bool>("HealthOptions:Enabled").Should().BeFalse();
        local.GetValue<bool>("Modules:Flight:BackgroundProcessingEnabled").Should().BeTrue();
        docker.GetValue<bool>("Modules:Flight:BackgroundProcessingEnabled").Should().BeTrue();
        local.GetValue<bool>("PersistMessageOptions:Enabled").Should().BeTrue();
        docker.GetValue<bool>("PersistMessageOptions:Enabled").Should().BeTrue();

        new Uri(docker["Jwt:MetadataAddress"]!).Host.Should().Be("booking_modular_monolith");
    }

    [Fact]
    public void postgres_init_script_creates_flight_service_role_and_database_with_configured_password()
    {
        var root = FindRepositoryRoot();
        var configuration = LoadConfiguration(root, docker: false);
        var postgres = new NpgsqlConnectionStringBuilder(configuration["PostgresOptions:ConnectionString:Flight"]);
        var script = File.ReadAllText(
            Path.Combine(root, "deployments", "docker-compose", "init", "postgres", "01-service-databases.sql")
        );
        var role = Regex.Match(
            script,
            @"CREATE\s+ROLE\s+flight_service\s+LOGIN\s+PASSWORD\s+'([^']+)'",
            RegexOptions.IgnoreCase
        );

        role.Success.Should().BeTrue();
        role.Groups[1].Value.Should().Be(postgres.Password);
        Regex
            .IsMatch(script, @"CREATE\s+DATABASE\s+flight_service\s+OWNER\s+flight_service", RegexOptions.IgnoreCase)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void dockerfile_copies_all_host_project_references_before_restore()
    {
        var root = FindRepositoryRoot();
        var dockerfile = File.ReadAllText(Path.Combine(root, "src", "Services", "Flight.Host", "Dockerfile"));
        var lines = dockerfile.Split('\n');
        var restoreLine = Array.FindIndex(
            lines,
            line => line.Trim().StartsWith("RUN dotnet restore", StringComparison.Ordinal)
        );
        restoreLine.Should().BeGreaterThanOrEqualTo(0);

        var copyLines = lines.Where(line => line.TrimStart().StartsWith("COPY ", StringComparison.Ordinal)).ToArray();
        var allCopiedSources = copyLines.SelectMany(GetCopySources).ToArray();
        var copiedSources = lines
            .Take(restoreLine)
            .Where(line => line.TrimStart().StartsWith("COPY ", StringComparison.Ordinal))
            .SelectMany(GetCopySources)
            .ToHashSet(StringComparer.Ordinal);
        var copiedProjectFiles = copiedSources
            .Where(path =>
                path.EndsWith(".csproj", StringComparison.Ordinal) || path.EndsWith(".props", StringComparison.Ordinal)
            )
            .ToHashSet(StringComparer.Ordinal);

        allCopiedSources
            .Where(path => path.StartsWith("src/", StringComparison.Ordinal))
            .Where(path =>
                path.EndsWith(".csproj", StringComparison.Ordinal) || path.EndsWith(".props", StringComparison.Ordinal)
            )
            .Select(path => Path.Combine(root, path))
            .Should()
            .OnlyContain(path => File.Exists(path));

        var closure = ProjectReferenceClosure(
            Path.Combine(root, "src", "Services", "Flight.Host", "Flight.Host.csproj")
        );
        closure.Should().NotBeEmpty();
        closure
            .Select(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Should()
            .OnlyContain(path => copiedProjectFiles.Contains(path));

        dockerfile.Should().Contain("ENV ASPNETCORE_ENVIRONMENT=docker");
        dockerfile.Should().Contain("EXPOSE 80 81");
        dockerfile.Should().Contain("ENTRYPOINT [\"dotnet\", \"Flight.Host.dll\"]");
        foreach (
            var forbidden in new[] { "src/Api", "src/Modules/Booking", "src/Modules/Passenger", "src/Modules/Identity" }
        )
        {
            lines
                .Where(line => line.TrimStart().StartsWith("COPY ", StringComparison.Ordinal))
                .Should()
                .OnlyContain(line => !line.Contains(forbidden, StringComparison.Ordinal));
        }
    }

    private static void AssertKestrel(IConfiguration configuration, string httpUrl, string grpcUrl)
    {
        configuration["Kestrel:Endpoints:Http:Url"].Should().Be(httpUrl);
        configuration["Kestrel:Endpoints:Http:Protocols"].Should().Be("Http1AndHttp2");
        configuration["Kestrel:Endpoints:Grpc:Url"].Should().Be(grpcUrl);
        configuration["Kestrel:Endpoints:Grpc:Protocols"].Should().Be("Http2");
    }

    private static void AssertPostgres(IConfiguration configuration, string host)
    {
        var connection = new NpgsqlConnectionStringBuilder(configuration["PostgresOptions:ConnectionString:Flight"]);

        connection.Database.Should().Be("flight_service");
        connection.Username.Should().Be("flight_service");
        connection.Host.Should().Be(host);
    }

    private static void AssertMongo(IConfiguration configuration)
    {
        configuration["MongoOptions:Flight:DatabaseName"].Should().Be("flight_service_read");
        if (configuration["Kestrel:Endpoints:Http:Url"] == "http://+:80")
            configuration["MongoOptions:ConnectionString"].Should().Be("mongodb://mongo:27017");
    }

    private static IConfiguration LoadConfiguration(string root, bool docker)
    {
        var hostDirectory = Path.Combine(root, "src", "Services", "Flight.Host");
        var builder = new ConfigurationBuilder().AddJsonFile(Path.Combine(hostDirectory, "appsettings.json"));
        if (docker)
            builder.AddJsonFile(Path.Combine(hostDirectory, "appsettings.docker.json"));
        return builder.Build();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "booking-modular-monolith.sln")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new DirectoryNotFoundException(
                "Could not find booking-modular-monolith.sln from the test directory."
            );
    }

    private static IEnumerable<string> GetCopySources(string line)
    {
        var fields = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).ToArray();
        return fields.Take(fields.Length - 1);
    }

    private static HashSet<string> ProjectReferenceClosure(string rootProject)
    {
        var discovered = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(rootProject));

        while (pending.TryPop(out var project))
        {
            if (!discovered.Add(project))
                continue;

            foreach (var importedFile in ImportedFiles(project))
            {
                foreach (var reference in ProjectReferences(importedFile))
                {
                    var referencePath = ResolveInclude(importedFile, reference);
                    if (!File.Exists(referencePath))
                        throw new FileNotFoundException($"ProjectReference target does not exist: {referencePath}");
                    pending.Push(referencePath);
                }
            }
        }

        return discovered;
    }

    private static IEnumerable<string> ImportedFiles(string project)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(project);

        while (pending.TryPop(out var current))
        {
            if (!visited.Add(current))
                continue;

            yield return current;
            var document = System.Xml.Linq.XDocument.Load(current);
            foreach (var import in document.Descendants("Import"))
            {
                var include = import.Attribute("Project")?.Value;
                if (include is null)
                    continue;

                var imported = ResolveInclude(current, include);
                if (File.Exists(imported))
                    pending.Push(imported);
            }
        }
    }

    private static IEnumerable<string> ProjectReferences(string project)
    {
        var document = System.Xml.Linq.XDocument.Load(project);
        return document
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .OfType<string>();
    }

    private static string ResolveInclude(string sourceFile, string include)
    {
        var directory = Path.GetDirectoryName(sourceFile)!;
        include = include.Replace('\\', Path.DirectorySeparatorChar);
        include = include.Replace(
            "$(MSBuildThisFileDirectory)",
            directory + Path.DirectorySeparatorChar,
            StringComparison.Ordinal
        );
        return Path.GetFullPath(Path.Combine(directory, include));
    }
}
