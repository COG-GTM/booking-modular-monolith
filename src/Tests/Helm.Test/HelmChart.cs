using System.Diagnostics;
using YamlDotNet.RepresentationModel;

namespace Helm.Test;

/// <summary>Renders <c>deployments/helm/booking</c> with <c>helm template</c> and exposes the resulting objects.</summary>
public static class HelmChart
{
    public static readonly string RepoRoot = FindRepoRoot();
    public static readonly string ChartDir = Path.Combine(RepoRoot, "deployments", "helm", "booking");
    public static readonly string KubernetesDir = Path.Combine(RepoRoot, "deployments", "kubernetes");

    public static bool IsHelmAvailable { get; } = ProbeHelm();

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<
        string,
        Lazy<IReadOnlyList<K8sObject>>
    > Cache = new(StringComparer.Ordinal);

    public static IReadOnlyList<K8sObject> Render(params string[] args) =>
        Cache
            .GetOrAdd(string.Join('\u001f', args), _ => new Lazy<IReadOnlyList<K8sObject>>(() => RenderUncached(args)))
            .Value;

    private static IReadOnlyList<K8sObject> RenderUncached(string[] args)
    {
        var (exitCode, stdout, stderr) = Run("template", "booking", ChartDir, args);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"helm template failed ({exitCode}): {stderr}");
        }

        return Parse(stdout);
    }

    public static string RenderError(params string[] args)
    {
        var (exitCode, _, stderr) = Run("template", "booking", ChartDir, args);
        if (exitCode == 0)
        {
            throw new InvalidOperationException("helm template succeeded but a render failure was expected");
        }

        return stderr;
    }

    public static (int ExitCode, string Stdout, string Stderr) Lint(params string[] args) =>
        Run("lint", ChartDir, null, args);

    public static string ValuesFile(string name) => Path.Combine(ChartDir, name);

    public static YamlMappingNode LoadYaml(string path)
    {
        using var reader = new StreamReader(path);
        var stream = new YamlStream();
        stream.Load(reader);
        return (YamlMappingNode)stream.Documents[0].RootNode;
    }

    private static IReadOnlyList<K8sObject> Parse(string manifest)
    {
        using var reader = new StringReader(manifest);
        return new LenientYaml().LoadAll(reader).OfType<YamlMappingNode>().Select(root => new K8sObject(root)).ToList();
    }

    private static (int ExitCode, string Stdout, string Stderr) Run(
        string verb,
        string first,
        string? second,
        string[] args
    )
    {
        var psi = new ProcessStartInfo("helm")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = ChartDir,
        };
        psi.ArgumentList.Add(verb);
        psi.ArgumentList.Add(first);
        if (second is not null)
        {
            psi.ArgumentList.Add(second);
        }

        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("failed to start helm");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, stdoutTask.GetAwaiter().GetResult(), stderrTask.GetAwaiter().GetResult());
    }

    private static bool ProbeHelm()
    {
        try
        {
            var psi = new ProcessStartInfo("helm")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("version");
            using var process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "booking-modular-monolith.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("repository root (booking-modular-monolith.sln) not found");
    }
}
