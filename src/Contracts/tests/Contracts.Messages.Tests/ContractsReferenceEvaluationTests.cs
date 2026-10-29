using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Contracts.Messages.Tests;

public class ContractsReferenceEvaluationTests
{
    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(true, "2.3.4")]
    public async Task consumer_uses_either_project_or_exactly_pinned_package(bool packageMode, string? version)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = ContractTypes.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("msbuild");
        start.ArgumentList.Add("src/Modules/Identity/src/Identity.csproj");
        start.ArgumentList.Add("-nologo");
        start.ArgumentList.Add($"-p:UseContractsPackage={packageMode.ToString().ToLowerInvariant()}");
        start.ArgumentList.Add("-getProperty:ContractsMessagesPackageVersion");
        start.ArgumentList.Add("-getItem:PackageReference,ProjectReference");
        if (version is not null)
            start.ArgumentList.Add($"-p:ContractsMessagesPackageVersion={version}");

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        process.ExitCode.Should().Be(0, await errors);

        using var evaluated = JsonDocument.Parse(await output);
        var root = evaluated.RootElement;
        var pin = root.GetProperty("Properties").GetProperty("ContractsMessagesPackageVersion").GetString();
        pin.Should().NotBeNullOrEmpty();
        if (version is not null)
            pin.Should().Be(version);
        var items = root.GetProperty("Items");
        var packages = items
            .GetProperty("PackageReference")
            .EnumerateArray()
            .Where(item => item.GetProperty("Identity").GetString() == "BookingModularMonolith.Contracts.Messages")
            .ToList();
        var projects = items
            .GetProperty("ProjectReference")
            .EnumerateArray()
            .Where(item => item.GetProperty("Filename").GetString() == "Contracts.Messages")
            .ToList();

        if (packageMode)
        {
            packages.Should().ContainSingle().Which.GetProperty("Version").GetString().Should().Be($"[{pin}]");
            projects.Should().BeEmpty();
        }
        else
        {
            packages.Should().BeEmpty();
            projects
                .Should()
                .ContainSingle()
                .Which.GetProperty("FullPath")
                .GetString()
                .Should()
                .Be(
                    Path.Combine(
                        ContractTypes.RepoRoot,
                        "src",
                        "Contracts",
                        "Contracts.Messages",
                        "Contracts.Messages.csproj"
                    )
                );
        }
    }
}
