using FluentAssertions;
using Xunit;

namespace Deployment.Test;

public class ServicesDockerfileTests
{
    private static readonly string[] Lines = File.ReadAllLines(RepositoryPaths.ServicesDockerfile);

    [Fact]
    public void should_declare_service_build_args_in_builder_stage()
    {
        var builderStage = GetStage("builder");

        builderStage.Should().Contain("ARG SERVICE_DIR");
        builderStage.Should().Contain("ARG SERVICE_NAME");
    }

    [Fact]
    public void should_restore_build_and_publish_the_parameterized_service_project()
    {
        var expectedProject = "src/Services/${SERVICE_DIR}/${SERVICE_NAME}.csproj";

        Lines.Should().Contain(l => l.StartsWith("RUN dotnet restore ") && l.Contains(expectedProject));
        Lines.Should().Contain(l => l.StartsWith("RUN dotnet build ") && l.Contains(expectedProject));
        Lines.Should().Contain(l => l.StartsWith("RUN dotnet publish ") && l.Contains(expectedProject));
    }

    [Theory]
    [InlineData("src/Services/Flight/FlightService.csproj")]
    [InlineData("src/Services/Passenger/PassengerService.csproj")]
    [InlineData("src/Services/Identity/IdentityService.csproj")]
    [InlineData("src/Services/Booking/BookingService.csproj")]
    [InlineData("src/Services/Gateway/GatewayService.csproj")]
    public void should_copy_every_service_project_file_for_layer_caching(string projectFile)
    {
        Lines.Should().Contain(l => l.StartsWith("COPY ") && l.Contains(projectFile));
        File.Exists(Path.Combine(RepositoryPaths.Root, projectFile.Replace('/', Path.DirectorySeparatorChar)))
            .Should()
            .BeTrue($"the Dockerfile copies {projectFile}, so it must exist in the repository");
    }

    [Fact]
    public void should_run_parameterized_service_dll_on_http_port_80_in_runtime_stage()
    {
        var runtimeStage = GetStage("runtime");

        runtimeStage.Should().Contain("ARG SERVICE_NAME");
        runtimeStage.Should().Contain("ENV SERVICE_DLL=${SERVICE_NAME}.dll");
        runtimeStage.Should().Contain("ENV ASPNETCORE_URLS=http://+:80");
        runtimeStage.Should().Contain("ENV ASPNETCORE_ENVIRONMENT=docker");
        runtimeStage.Should().Contain("EXPOSE 80");
        runtimeStage
            .Should()
            .Contain(l => l.StartsWith("ENTRYPOINT ") && l.Contains("dotnet") && l.Contains("${SERVICE_DLL}"));
    }

    private static List<string> GetStage(string stage)
    {
        var stageStarts = Lines
            .Select((line, index) => (line, index))
            .Where(x => x.line.StartsWith("FROM ", StringComparison.Ordinal))
            .ToList();

        stageStarts.Should().HaveCount(2, "the Dockerfile is expected to have a builder stage and a runtime stage");

        var (start, end) = stage == "builder"
            ? (stageStarts[0].index, stageStarts[1].index)
            : (stageStarts[1].index, Lines.Length);

        return Lines[start..end].Select(l => l.Trim()).ToList();
    }
}
