using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Flight.Host.Test;

// Runs the published Flight.Host.dll as a separate OS process (real Kestrel, HTTP/1.1 REST port +
// HTTP/2-only gRPC port) against the fixture's Testcontainers, using fresh flight_service databases.
public sealed class FlightHostProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly StringBuilder _output = new();

    private FlightHostProcess(Process process, int httpPort, int grpcPort)
    {
        _process = process;
        HttpAddress = new Uri($"http://127.0.0.1:{httpPort}");
        GrpcAddress = new Uri($"http://127.0.0.1:{grpcPort}");
    }

    public Uri HttpAddress { get; }
    public Uri GrpcAddress { get; }

    public string Output
    {
        get
        {
            lock (_output)
            {
                return _output.ToString();
            }
        }
    }

    public static async Task<FlightHostProcess> StartAsync(IConfiguration fixtureConfiguration)
    {
        var hostDll = typeof(Program).Assembly.Location;
        var httpPort = GetFreePort();
        var grpcPort = GetFreePort();

        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            WorkingDirectory = Path.GetDirectoryName(hostDll)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(hostDll);

        var env = startInfo.Environment;
        env["ASPNETCORE_ENVIRONMENT"] = "Development";
        env["Kestrel__Endpoints__Http__Url"] = $"http://127.0.0.1:{httpPort}";
        env["Kestrel__Endpoints__Grpc__Url"] = $"http://127.0.0.1:{grpcPort}";
        env["PostgresOptions__ConnectionString__Flight"] = WithDatabase(
            fixtureConfiguration["PostgresOptions:ConnectionString:Flight"]!,
            "flight_service"
        );
        env["PersistMessageOptions__ConnectionString"] = WithDatabase(
            fixtureConfiguration["PersistMessageOptions:ConnectionString"]!,
            "flight_service_persist_message"
        );
        env["MongoOptions__ConnectionString"] = fixtureConfiguration["MongoOptions:ConnectionString"];
        env["MongoOptions__DatabaseName"] = "flight_service_read";
        env["MessageBroker__TransportType"] = "RabbitMq";
        env["MessageBroker__RabbitMq__Host"] = fixtureConfiguration["MessageBroker:RabbitMq:Host"];
        env["MessageBroker__RabbitMq__Port"] = fixtureConfiguration["MessageBroker:RabbitMq:Port"];
        env["MessageBroker__RabbitMq__Username"] = fixtureConfiguration["MessageBroker:RabbitMq:Username"];
        env["MessageBroker__RabbitMq__Password"] = fixtureConfiguration["MessageBroker:RabbitMq:Password"];
        env["ObservabilityOptions__UseOTLPExporter"] = "false";
        env["ObservabilityOptions__UseAspireOTLPExporter"] = "false";

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var host = new FlightHostProcess(process, httpPort, grpcPort);
        process.OutputDataReceived += (_, e) => host.Append(e.Data);
        process.ErrorDataReceived += (_, e) => host.Append(e.Data);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await host.WaitUntilReadyAsync(TimeSpan.FromSeconds(90));

        return host;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }

        _process.Dispose();
    }

    private async Task WaitUntilReadyAsync(TimeSpan timeout)
    {
        using var client = new HttpClient { BaseAddress = HttpAddress, Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (_process.HasExited)
            {
                throw new InvalidOperationException(
                    $"Flight.Host exited with code {_process.ExitCode} before becoming ready:\n{Output}"
                );
            }

            try
            {
                var response = await client.GetAsync("/");

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return;
                }
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }

            await Task.Delay(500);
        }

        throw new TimeoutException($"Flight.Host did not become ready within {timeout}:\n{Output}");
    }

    private void Append(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_output)
        {
            _output.AppendLine(line);
        }
    }

    private static string WithDatabase(string connectionString, string database) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Database = database }.ConnectionString;

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
