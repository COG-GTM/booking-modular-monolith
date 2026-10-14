using FluentAssertions;
using Xunit;

namespace Helm.Test;

public class ChartLintTests
{
    [HelmFact]
    public void chart_lints_cleanly_with_default_values()
    {
        var (exitCode, stdout, stderr) = HelmChart.Lint();

        exitCode.Should().Be(0, stdout + stderr);
        stdout.Should().Contain("1 chart(s) linted, 0 chart(s) failed");
    }

    [HelmFact]
    public void chart_lints_cleanly_with_the_kind_overlay()
    {
        var (exitCode, stdout, stderr) = HelmChart.Lint("-f", HelmChart.ValuesFile("values-kind.yaml"));

        exitCode.Should().Be(0, stdout + stderr);
        stdout.Should().Contain("1 chart(s) linted, 0 chart(s) failed");
    }

    [HelmFact]
    public void chart_lints_cleanly_with_the_production_overlay()
    {
        var (exitCode, stdout, stderr) = HelmChart.Lint(
            "-f",
            HelmChart.ValuesFile("values-production.yaml"),
            "--set",
            "global.image.tag=abc123"
        );

        exitCode.Should().Be(0, stdout + stderr);
        stdout.Should().Contain("1 chart(s) linted, 0 chart(s) failed");
    }
}
