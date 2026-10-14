using Xunit;

namespace Helm.Test;

/// <summary>Runs only when the <c>helm</c> CLI is on PATH; otherwise the test is reported as skipped.</summary>
public sealed class HelmFactAttribute : FactAttribute
{
    public HelmFactAttribute()
    {
        if (!HelmChart.IsHelmAvailable)
        {
            Skip = "helm CLI not found on PATH";
        }
    }
}
