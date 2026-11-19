using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Contracts.Grpc.Tests;

public class GrpcReferenceEvaluationTests
{
    [Theory]
    [InlineData("Booking", false, null)]
    [InlineData("Flight", false, null)]
    [InlineData("Passenger", false, null)]
    [InlineData("Booking", true, null)]
    [InlineData("Flight", true, null)]
    [InlineData("Passenger", true, null)]
    [InlineData("Booking", true, "2.3.4")]
    [InlineData("Flight", true, "2.3.4")]
    [InlineData("Passenger", true, "2.3.4")]
    public async Task modules_use_either_the_shared_project_or_an_exact_package_pin(
        string module,
        bool packageMode,
        string? version
    )
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "booking-modular-monolith.sln")))
            directory = directory.Parent;
        var root = directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("msbuild");
        start.ArgumentList.Add($"src/Modules/{module}/src/{module}.csproj");
        start.ArgumentList.Add("-nologo");
        start.ArgumentList.Add($"-p:UseContractsGrpcPackage={packageMode.ToString().ToLowerInvariant()}");
        start.ArgumentList.Add("-getProperty:ContractsGrpcPackageVersion,ContractsGrpcVersion");
        start.ArgumentList.Add("-getItem:PackageReference,ProjectReference");
        if (version is not null)
            start.ArgumentList.Add($"-p:ContractsGrpcPackageVersion={version}");

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
        var properties = evaluated.RootElement.GetProperty("Properties");
        var pin = properties.GetProperty("ContractsGrpcPackageVersion").GetString();
        pin.Should().NotBeNullOrEmpty();
        pin.Should().Be(version ?? properties.GetProperty("ContractsGrpcVersion").GetString());
        var items = evaluated.RootElement.GetProperty("Items");
        var packages = items
            .GetProperty("PackageReference")
            .EnumerateArray()
            .Where(item => item.GetProperty("Identity").GetString() == "BookingModularMonolith.Contracts.Grpc")
            .ToList();
        var projects = items
            .GetProperty("ProjectReference")
            .EnumerateArray()
            .Where(item => item.GetProperty("Filename").GetString() == "Contracts.Grpc")
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
                .Be(Path.Combine(root, "src", "Contracts", "Contracts.Grpc", "Contracts.Grpc.csproj"));
        }
    }
}
