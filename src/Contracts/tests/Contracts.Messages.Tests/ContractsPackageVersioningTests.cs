using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace Contracts.Messages.Tests;

// Verifies the contracts package is versioned independently of BuildingBlocks (MSBuild-evaluated).
public class ContractsPackageVersioningTests
{
    private const string ContractPackageId = "BookingModularMonolith.Contracts.Messages";

    [Fact]
    public void contracts_project_packs_with_its_own_identity_and_version()
    {
        var expectedVersion = ReadProperty("src/Contracts/Versions.props", "ContractsMessagesVersion");
        var evaluation = Evaluate("src/Contracts/Contracts.Messages/Contracts.Messages.csproj");
        var overrideEvaluation = Evaluate(
            "src/Contracts/Contracts.Messages/Contracts.Messages.csproj",
            "-p:ContractsMessagesVersion=2.0.0-preview.1"
        );

        evaluation["PackageId"].Should().Be(ContractPackageId);
        evaluation["Version"].Should().Be(expectedVersion);
        evaluation["IsPackable"].Should().Be("true");
        overrideEvaluation["Version"].Should().Be("2.0.0-preview.1");
    }

    [Fact]
    public void buildingblocks_version_override_does_not_reversion_the_contracts()
    {
        var contractsVersion = ReadProperty("src/Contracts/Versions.props", "ContractsMessagesVersion");
        var buildingBlocksVersion = ReadProperty("src/BuildingBlocks/Directory.Build.props", "BuildingBlocksVersion");
        var contracts = Evaluate(
            "src/Contracts/Contracts.Messages/Contracts.Messages.csproj",
            "-p:BuildingBlocksVersion=9.9.9"
        );
        var coreWithOverride = Evaluate(
            "src/BuildingBlocks/Core/BuildingBlocks.Core.csproj",
            "-p:BuildingBlocksVersion=9.9.9"
        );
        var coreWithoutOverride = Evaluate("src/BuildingBlocks/Core/BuildingBlocks.Core.csproj");

        contracts["Version"].Should().Be(contractsVersion);
        coreWithOverride["Version"].Should().Be("9.9.9");
        coreWithoutOverride["Version"].Should().Be(buildingBlocksVersion);
    }

    private static IReadOnlyDictionary<string, string> Evaluate(string project, params string[] properties)
    {
        var projectPath = Path.Combine(ContractTypes.RepoRoot, project);
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            WorkingDirectory = ContractTypes.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("-nologo");
        startInfo.ArgumentList.Add("-getProperty:PackageId,Version,IsPackable");
        foreach (var property in properties)
            startInfo.ArgumentList.Add(property);
        foreach (
            var name in startInfo
                .Environment.Keys.Where(name => name.StartsWith("MSBUILD", StringComparison.OrdinalIgnoreCase))
                .ToList()
        )
            startInfo.Environment.Remove(name);

        using var process = Process.Start(startInfo)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            process.Kill(true);
            process.WaitForExit();
            throw new TimeoutException($"MSBuild evaluation timed out for {projectPath}.");
        }

        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        process
            .ExitCode.Should()
            .Be(0, "MSBuild evaluation failed for {0}\nstdout:\n{1}\nstderr:\n{2}", projectPath, stdout, stderr);

        using var document = JsonDocument.Parse(stdout);
        return document
            .RootElement.GetProperty("Properties")
            .EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.GetString() ?? "");
    }

    private static string ReadProperty(string path, string property)
    {
        var document = XDocument.Load(Path.Combine(ContractTypes.RepoRoot, path));
        return document.Descendants(property).Single().Value;
    }
}
