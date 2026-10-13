using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using BuildingBlocks.TestBase;
using MassTransit;
using Npgsql;
using Testcontainers.MongoDb;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Xunit;

namespace Passenger.Host.Test;

// Runs the published Passenger.Host.dll as a separate OS process against its own containers.
public sealed class PassengerHostProcessFixture : IAsyncLifetime
{
    public const string PassengerDatabase = "passenger_service";
    public const string PersistMessageDatabase = "passenger_service_persist_message";
    public const string ReadDatabase = "passenger_service_read";

    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(120);

    private readonly PostgreSqlContainer _postgres = TestContainers.PostgresTestContainer();
    private readonly MongoDbContainer _mongo = TestContainers.MongoTestContainer();
    private readonly RabbitMqContainer _rabbitMq = TestContainers.RabbitMqTestContainer();
    private readonly StringBuilder _output = new();
    private Process? _process;

    public Uri HttpAddress { get; private set; } = null!;
    public Uri GrpcAddress { get; private set; } = null!;
    public string PassengerConnectionString { get; private set; } = null!;
    public string MongoConnectionString => _mongo.GetConnectionString();

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

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _mongo.StartAsync(), _rabbitMq.StartAsync());

        PassengerConnectionString = WithDatabase(_postgres.GetConnectionString(), PassengerDatabase);
        HttpAddress = new Uri($"http://127.0.0.1:{GetFreePort()}");
        GrpcAddress = new Uri($"http://127.0.0.1:{GetFreePort()}");

        var hostDll = Path.Combine(AppContext.BaseDirectory, "Passenger.Host.dll");
        var startInfo = new ProcessStartInfo("dotnet", $"\"{hostDll}\"")
        {
            WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var (key, value) in HostEnvironment())
        {
            startInfo.Environment[key] = value;
        }

        _process = Process.Start(startInfo) ?? throw new InvalidOperationException("Passenger.Host did not start.");
        _process.OutputDataReceived += (_, e) => Append(e.Data);
        _process.ErrorDataReceived += (_, e) => Append(e.Data);
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        await WaitUntilReadyAsync();
    }

    public async Task DisposeAsync()
    {
        if (_process is { HasExited: false })
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }

        _process?.Dispose();
        await Task.WhenAll(
            _postgres.DisposeAsync().AsTask(),
            _mongo.DisposeAsync().AsTask(),
            _rabbitMq.DisposeAsync().AsTask()
        );
    }

    public IBusControl CreateExternalPublisher() =>
        Bus.Factory.CreateUsingRabbitMq(cfg =>
            cfg.Host(
                _rabbitMq.Hostname,
                _rabbitMq.GetMappedPublicPort(TestContainers.RabbitMqContainerConfiguration.Port),
                "/",
                h =>
                {
                    h.Username(TestContainers.RabbitMqContainerConfiguration.UserName);
                    h.Password(TestContainers.RabbitMqContainerConfiguration.Password);
                }
            )
        );

    private IEnumerable<(string Key, string Value)> HostEnvironment()
    {
        yield return ("ASPNETCORE_ENVIRONMENT", "Production");
        yield return ("Kestrel__Endpoints__Http__Url", HttpAddress.ToString());
        yield return ("Kestrel__Endpoints__Grpc__Url", GrpcAddress.ToString());
        yield return ("PostgresOptions__ConnectionString__Passenger", PassengerConnectionString);
        yield return (
            "PersistMessageOptions__ConnectionString__Passenger",
            WithDatabase(_postgres.GetConnectionString(), PersistMessageDatabase)
        );
        yield return ("MongoOptions__ConnectionString", _mongo.GetConnectionString());
        yield return ("MongoOptions__Passenger__DatabaseName", ReadDatabase);
        yield return ("MessageBroker__TransportType", "RabbitMq");
        yield return ("MessageBroker__ServiceName", "passenger");
        yield return ("MessageBroker__UseInbox", "true");
        yield return ("MessageBroker__RabbitMq__Host", _rabbitMq.Hostname);
        yield return (
            "MessageBroker__RabbitMq__Port",
            _rabbitMq
                .GetMappedPublicPort(TestContainers.RabbitMqContainerConfiguration.Port)
                .ToString(CultureInfo.InvariantCulture)
        );
        yield return ("MessageBroker__RabbitMq__Username", TestContainers.RabbitMqContainerConfiguration.UserName);
        yield return ("MessageBroker__RabbitMq__Password", TestContainers.RabbitMqContainerConfiguration.Password);
        yield return ("ObservabilityOptions__UseOTLPExporter", "false");
        yield return ("ObservabilityOptions__UseAspireOTLPExporter", "false");
    }

    private async Task WaitUntilReadyAsync()
    {
        using var http = new HttpClient { BaseAddress = HttpAddress, Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow + StartupTimeout;

        while (DateTime.UtcNow < deadline)
        {
            if (_process!.HasExited)
            {
                throw new InvalidOperationException(
                    $"Passenger.Host exited with code {_process.ExitCode}:{Environment.NewLine}{Output}"
                );
            }

            try
            {
                var response = await http.GetAsync("/health/ready");
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return;
                }
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new TimeoutException($"Passenger.Host was not ready in {StartupTimeout}:{Environment.NewLine}{Output}");
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

[CollectionDefinition(Name)]
public class PassengerHostProcessCollection : ICollectionFixture<PassengerHostProcessFixture>
{
    public const string Name = "Passenger.Host process";
}
