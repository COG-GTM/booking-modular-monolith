namespace Microservices.EndToEnd.Test;

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

// One service host (or the gateway) running as its own OS process on real Kestrel ports.
public sealed class HostProcess : IAsyncDisposable
{
    private readonly Process process;
    private readonly StreamWriter log;

    private HostProcess(string name, Process process, StreamWriter log, string logPath)
    {
        Name = name;
        this.process = process;
        this.log = log;
        LogPath = logPath;
    }

    public string Name { get; }

    public string LogPath { get; }

    public static async Task<HostProcess> StartAsync(
        string name,
        string projectDirectory,
        string assemblyName,
        Uri readinessUrl,
        IReadOnlyDictionary<string, string> environment,
        string logDirectory
    )
    {
        var dll = Path.Combine(HostPaths.OutputDirectory(projectDirectory), $"{assemblyName}.dll");
        if (!File.Exists(dll))
        {
            throw new FileNotFoundException($"{name}: build the host first, {dll} not found", dll);
        }

        var startInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(dll)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(dll);
        foreach (var (key, value) in environment)
        {
            startInfo.Environment[key] = value;
        }

        var logPath = Path.Combine(logDirectory, $"{name}.log");
        var log = new StreamWriter(logPath, append: false) { AutoFlush = true };
        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => Write(log, e.Data);
        process.ErrorDataReceived += (_, e) => Write(log, e.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var host = new HostProcess(name, process, log, logPath);
        try
        {
            await host.WaitUntilReadyAsync(readinessUrl, TimeSpan.FromSeconds(180));
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }

        return host;
    }

    public static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public string Tail(int lines = 80) =>
        File.Exists(LogPath)
            ? string.Join(Environment.NewLine, ReadShared(LogPath).TakeLast(lines))
            : $"{Name}: no log written";

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
        catch (InvalidOperationException) { }

        process.Dispose();
        lock (log)
        {
            log.Dispose();
        }
    }

    private async Task WaitUntilReadyAsync(Uri url, TimeSpan timeout)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var last = "no response";
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException($"{Name} exited with {process.ExitCode}:\n{Tail()}");
            }

            try
            {
                using var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }

                last = $"HTTP {(int)response.StatusCode}";
            }
            catch (HttpRequestException exception)
            {
                last = exception.Message;
            }
            catch (TaskCanceledException)
            {
                last = "timeout";
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"{Name} not ready at {url} within {timeout} (last: {last}):\n{Tail()}");
    }

    private static void Write(StreamWriter log, string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (log)
        {
            if (log.BaseStream.CanWrite)
            {
                log.WriteLine(line);
            }
        }
    }

    private static IEnumerable<string> ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }
}

public static class HostPaths
{
    public static string RepositoryRoot { get; } = FindRoot();

    public static string Configuration =>
        typeof(HostPaths)
            .Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyConfigurationAttribute), false)
            .OfType<System.Reflection.AssemblyConfigurationAttribute>()
            .FirstOrDefault()
            ?.Configuration ?? "Debug";

    public static string OutputDirectory(string projectDirectory) =>
        Path.Combine(RepositoryRoot, projectDirectory, "bin", Configuration, "net10.0");

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "booking-modular-monolith.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("booking-modular-monolith.sln not found");
    }
}
