using System.Diagnostics;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace Contracts.Messages.Tests;

// Exercises packaging scripts with a fake dotnet executable and isolated temporary feeds.
public class PackagingScriptsTests
{
    [Fact]
    public void pack_contracts_rejects_a_non_semver_version()
    {
        using var environment = new FakeDotnetEnvironment();

        var result = environment.RunScript("scripts/pack-contracts.sh", "1.0");

        result.ExitCode.Should().Be(1);
        result.StandardError.Should().Contain("is not a SemVer 2.0 version");
        environment.Calls.Should().BeEmpty();
    }

    [Fact]
    public void pack_contracts_passes_the_requested_version()
    {
        using var environment = new FakeDotnetEnvironment();

        var result = environment.RunScript("scripts/pack-contracts.sh", "1.2.0-preview.1");
        var calls = environment.Calls;

        result.ExitCode.Should().Be(0, result.StandardOutput + result.StandardError);
        calls.Should().ContainSingle();
        calls[0].Should().Contain("pack");
        calls[0].Should().Contain("Contracts.Messages/Contracts.Messages.csproj");
        calls[0].Should().Contain($"-o {environment.Feed}");
        calls[0].Should().Contain("-p:ContractsMessagesVersion=1.2.0-preview.1");
    }

    [Fact]
    public void pack_contracts_without_a_version_keeps_the_declared_one()
    {
        using var environment = new FakeDotnetEnvironment();

        var result = environment.RunScript("scripts/pack-contracts.sh", "");
        var calls = environment.Calls;

        result.ExitCode.Should().Be(0, result.StandardOutput + result.StandardError);
        calls.Should().ContainSingle();
        calls[0].Should().Contain("Contracts.Messages/Contracts.Messages.csproj");
        calls[0].Should().NotContain("-p:ContractsMessagesVersion=");
        calls[0].Should().NotContain("-p:Version=");
    }

    [Fact]
    public void pack_building_blocks_packs_contracts_first_without_reversioning_them()
    {
        using var environment = new FakeDotnetEnvironment();
        var expectedBuildingBlocksProjects = Directory
            .EnumerateFiles(
                Path.Combine(ContractTypes.RepoRoot, "src", "BuildingBlocks"),
                "BuildingBlocks*.csproj",
                SearchOption.AllDirectories
            )
            .Where(path =>
                !path.Contains(
                    $"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal
                )
            )
            .Order(StringComparer.Ordinal)
            .ToList();

        var result = environment.RunScript("scripts/pack-building-blocks.sh", "1.4.0");
        var calls = environment.Calls;

        result.ExitCode.Should().Be(0, result.StandardOutput + result.StandardError);
        calls.Should().HaveCount(expectedBuildingBlocksProjects.Count + 1);
        calls[0].Should().Contain("Contracts.Messages/Contracts.Messages.csproj");
        calls[0].Should().NotContain("-p:ContractsMessagesVersion=");
        calls[0].Should().NotContain("-p:Version=");
        calls
            .Skip(1)
            .Should()
            .OnlyContain(call =>
                call.Contains("BuildingBlocks", StringComparison.Ordinal)
                && call.Contains(".csproj", StringComparison.Ordinal)
                && call.Contains("-p:BuildingBlocksVersion=1.4.0", StringComparison.Ordinal)
                && !call.Contains("-p:Version=", StringComparison.Ordinal)
                && !call.Contains("BuildingBlocks.Contracts.csproj", StringComparison.Ordinal)
            );
        calls
            .Skip(1)
            .Select(call => Path.GetFileName(FindProjectPath(call)))
            .Should()
            .BeEquivalentTo(expectedBuildingBlocksProjects.Select(Path.GetFileName));
    }

    [Fact]
    public void verify_contracts_consumer_defaults_to_the_declared_contracts_version()
    {
        using var environment = new FakeDotnetEnvironment();
        var version = ReadProperty("src/Contracts/Versions.props", "ContractsMessagesVersion");

        var result = environment.RunScript("scripts/verify-contracts-consumer.sh", "");
        var calls = environment.Calls;
        var restoredProject = XDocument.Load(environment.RestoredProjectPath);
        var buildCalls = calls.Where(call => call.StartsWith("build ", StringComparison.Ordinal)).ToList();

        result.ExitCode.Should().Be(0, result.StandardOutput + result.StandardError);
        restoredProject
            .Descendants("PackageReference")
            .Where(reference => reference.Attribute("Include")?.Value == "BookingModularMonolith.Contracts.Messages")
            .Select(reference => reference.Attribute("Version")?.Value)
            .Should()
            .Contain($"[{version}]");
        buildCalls.Should().HaveCount(2);
        buildCalls
            .Should()
            .OnlyContain(call =>
                call.Contains("-p:UseContractsPackage=true", StringComparison.Ordinal)
                && call.Contains($"-p:ContractsMessagesPackageVersion={version}", StringComparison.Ordinal)
                && call.Contains($"-p:RestoreAdditionalProjectSources={environment.Feed}", StringComparison.Ordinal)
            );
        buildCalls.Should().Contain(call => call.Contains("Identity.csproj", StringComparison.Ordinal));
        buildCalls.Should().Contain(call => call.Contains("Passenger.csproj", StringComparison.Ordinal));
    }

    [Fact]
    public void verify_contracts_consumer_fails_when_the_restore_graph_pulls_in_infrastructure()
    {
        using var environment = new FakeDotnetEnvironment("""{"libraries":{"Npgsql/9.0.0":{}}}""");

        var result = environment.RunScript("scripts/verify-contracts-consumer.sh", "");

        result.ExitCode.Should().NotBe(0);
        result.StandardError.Should().Contain("pulls in BuildingBlocks or data-store packages");
        environment.Calls.Should().NotContain(call => call.StartsWith("build ", StringComparison.Ordinal));
    }

    [Fact]
    public void verify_building_blocks_consumer_defaults_to_the_declared_buildingblocks_version()
    {
        using var environment = new FakeDotnetEnvironment();
        var version = ReadProperty("src/BuildingBlocks/Directory.Build.props", "BuildingBlocksVersion");

        var result = environment.RunScript("scripts/verify-building-blocks-consumer.sh", "");
        var restoredProject = XDocument.Load(environment.RestoredProjectPath);

        result.ExitCode.Should().Be(0, result.StandardOutput + result.StandardError);
        var packageReferences = restoredProject.Descendants("PackageReference");
        packageReferences
            .Where(reference => reference.Attribute("Include")?.Value == "BookingModularMonolith.BuildingBlocks.Core")
            .Select(reference => reference.Attribute("Version")?.Value)
            .Should()
            .Contain(version);
        packageReferences
            .Where(reference => reference.Attribute("Include")?.Value == "BookingModularMonolith.BuildingBlocks.Web")
            .Select(reference => reference.Attribute("Version")?.Value)
            .Should()
            .Contain(version);
    }

    private static string ReadProperty(string path, string property)
    {
        var document = XDocument.Load(Path.Combine(ContractTypes.RepoRoot, path));
        return document.Descendants(property).Single().Value;
    }

    private static string FindProjectPath(string call) =>
        call.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .First(argument => argument.EndsWith(".csproj", StringComparison.Ordinal));

    private sealed class FakeDotnetEnvironment : IDisposable
    {
        private const string FakeDotnetScript = """
            #!/usr/bin/env bash
            set -euo pipefail
            printf '%s\n' "$*" >> "$FAKE_DOTNET_DIR/calls.log"
            case "$1" in
              restore)
                project="$2"
                project_dir="$(dirname "$project")"
                cp "$project" "$FAKE_DOTNET_DIR/restored.csproj"
                mkdir -p "$project_dir/obj"
                if [[ -n "${FAKE_ASSETS:-}" ]]; then
                  printf '%s' "$FAKE_ASSETS" > "$project_dir/obj/project.assets.json"
                else
                  printf '{}' > "$project_dir/obj/project.assets.json"
                fi
                ;;
              run)
                echo "consumed urn:message:BuildingBlocks.Contracts.EventBus.Messages:UserCreated from Contracts.Messages 1.0.0.0"
                echo "published SampledIntegration"
                ;;
              build)
                project="$2"
                version=""
                for argument in "$@"; do
                  case "$argument" in
                    -p:ContractsMessagesPackageVersion=*) version="${argument#*=}" ;;
                  esac
                done
                project_dir="$(dirname "$project")"
                mkdir -p "$project_dir/obj"
                printf '{"libraries":{"BookingModularMonolith.Contracts.Messages/%s":{}}}\n' "$version" \
                  > "$project_dir/obj/project.assets.json"
                ;;
              pack)
                ;;
            esac
            """;

        private readonly string _directory;

        public FakeDotnetEnvironment(string? assets = null)
        {
            _directory = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"contracts-packaging-tests-{Guid.NewGuid():N}"
            );
            var bin = System.IO.Path.Combine(_directory, "bin");
            Feed = System.IO.Path.Combine(_directory, "feed");
            Directory.CreateDirectory(bin);
            Directory.CreateDirectory(Feed);
            var executable = System.IO.Path.Combine(bin, "dotnet");
            File.WriteAllText(executable, FakeDotnetScript);
            File.SetUnixFileMode(
                executable,
                UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead
                    | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead
                    | UnixFileMode.OtherExecute
            );
            Path = bin + System.IO.Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
            Assets = assets;
        }

        public string Feed { get; }
        public string Path { get; }
        public string? Assets { get; }
        public string RestoredProjectPath => System.IO.Path.Combine(_directory, "restored.csproj");
        public IReadOnlyList<string> Calls =>
            File.Exists(System.IO.Path.Combine(_directory, "calls.log"))
                ? File.ReadAllLines(System.IO.Path.Combine(_directory, "calls.log"))
                : [];

        public ProcessResult RunScript(string script, string version)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "bash",
                WorkingDirectory = ContractTypes.RepoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(System.IO.Path.Combine(ContractTypes.RepoRoot, script));
            startInfo.ArgumentList.Add(version);
            startInfo.ArgumentList.Add(Feed);
            startInfo.Environment["PATH"] = Path;
            startInfo.Environment["FAKE_DOTNET_DIR"] = _directory;
            if (Assets is not null)
                startInfo.Environment["FAKE_ASSETS"] = Assets;

            using var process = Process.Start(startInfo)!;
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(120_000))
            {
                process.Kill(true);
                process.WaitForExit();
                throw new TimeoutException($"Script execution timed out: {script}");
            }

            return new ProcessResult(
                process.ExitCode,
                stdoutTask.GetAwaiter().GetResult(),
                stderrTask.GetAwaiter().GetResult()
            );
        }

        public void Dispose() => Directory.Delete(_directory, true);
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
