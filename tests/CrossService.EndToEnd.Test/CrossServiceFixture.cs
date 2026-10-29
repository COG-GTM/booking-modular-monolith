namespace CrossService.EndToEnd.Test;

using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Npgsql;
using Testcontainers.EventStoreDb;
using Testcontainers.MongoDb;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Xunit;

[CollectionDefinition("CrossService", DisableParallelization = true)]
public sealed class CrossServiceCollection : ICollectionFixture<CrossServiceFixture>
{
    public const string Name = "CrossService";
}

public sealed class CrossServiceFixture : IAsyncLifetime
{
    private const string PostgreSqlUser = "e2e";
    private const string PostgreSqlPassword = "e2e-password";
    private const string MongoUser = "e2e";
    private const string MongoPassword = "e2e-password";
    private const string RabbitUser = "e2e";
    private const string RabbitPassword = "e2e-password";
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private readonly List<HostProcess> _hosts = [];
    private readonly PostgreSqlContainer _postgres;
    private readonly MongoDbContainer _mongo;
    private readonly RabbitMqContainer _rabbit;
    private readonly EventStoreDbContainer _eventStore;
    private readonly string _repositoryRoot;
    private readonly string _buildConfiguration;
    private readonly string _logsDirectory;
    private readonly Dictionary<string, string> _connectionStrings = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _identityPort = FreePort();
    private readonly int _flightPort = FreePort();
    private readonly int _flightGrpcPort = FreePort();
    private readonly int _passengerPort = FreePort();
    private readonly int _passengerGrpcPort = FreePort();
    private readonly int _bookingPort = FreePort();
    private readonly int _gatewayPort = FreePort();

    public CrossServiceFixture()
    {
        var assembly = typeof(CrossServiceFixture).Assembly;
        _repositoryRoot = Path.GetFullPath(GetMetadata(assembly, "RepositoryRoot"));
        _buildConfiguration = GetMetadata(assembly, "BuildConfiguration");
        _logsDirectory = Path.Combine(AppContext.BaseDirectory, "TestResults", "e2e-logs");
        Directory.CreateDirectory(_logsDirectory);

        _postgres = new PostgreSqlBuilder()
            .WithImage(Image("E2E_POSTGRES_IMAGE", "postgres:16"))
            .WithDatabase("postgres")
            .WithUsername(PostgreSqlUser)
            .WithPassword(PostgreSqlPassword)
            .WithCommand("-c", "max_prepared_transactions=100")
            .Build();
        _mongo = new MongoDbBuilder()
            .WithImage(Image("E2E_MONGO_IMAGE", "mongo:latest"))
            .WithUsername(MongoUser)
            .WithPassword(MongoPassword)
            .Build();
        _rabbit = new RabbitMqBuilder()
            .WithImage(Image("E2E_RABBITMQ_IMAGE", "rabbitmq:management"))
            .WithUsername(RabbitUser)
            .WithPassword(RabbitPassword)
            .Build();
        _eventStore = new EventStoreDbBuilder()
            .WithImage(Image("E2E_EVENTSTORE_IMAGE", "eventstore/eventstore:latest"))
            .WithEnvironment("EVENTSTORE_INSECURE", "true")
            .WithEnvironment("EVENTSTORE_ALLOW_ANONYMOUS_ENDPOINT_ACCESS", "true")
            .WithEnvironment("EVENTSTORE_ALLOW_ANONYMOUS_STREAM_ACCESS", "true")
            .Build();
    }

    public string GatewayUrl => $"http://127.0.0.1:{_gatewayPort}";
    public MongoClient MongoClient => new(_mongo.GetConnectionString());
    public string BookingDatabaseName => "booking_service_read";

    public async Task InitializeAsync()
    {
        try
        {
            await Task.WhenAll(
                _postgres.StartAsync(),
                _mongo.StartAsync(),
                _rabbit.StartAsync(),
                _eventStore.StartAsync()
            );
            await InitializePostgresAsync();
            ConfigureDatabaseConnections();

            var identityUrl = $"http://127.0.0.1:{_identityPort}";
            var flightUrl = $"http://127.0.0.1:{_flightPort}";
            var passengerUrl = $"http://127.0.0.1:{_passengerPort}";
            var bookingUrl = $"http://127.0.0.1:{_bookingPort}";
            var grpcFlightUrl = $"http://127.0.0.1:{_flightGrpcPort}";
            var grpcPassengerUrl = $"http://127.0.0.1:{_passengerGrpcPort}";

            await StartAndWaitAsync(
                "identity",
                "Identity.Host",
                _identityPort,
                HostEnvironment(identityUrl, identityUrl)
                    .Concat(
                        [
                            Pair("AuthOptions__IssuerUri", GatewayUrl),
                            Pair("PostgresOptions__ConnectionString__Identity", _connectionStrings["identity"]),
                            Pair(
                                "PersistMessageOptions__ConnectionString__Identity",
                                _connectionStrings["identity_persist"]
                            ),
                        ]
                    )
            );
            await Task.WhenAll(
                StartAndWaitAsync(
                    "flight",
                    "Flight.Host",
                    _flightPort,
                    HostEnvironment(flightUrl, identityUrl)
                        .Concat(
                            [
                                Pair("Kestrel__Endpoints__Grpc__Url", grpcFlightUrl),
                                Pair("MongoOptions__Flight__DatabaseName", "flight_service_read"),
                                Pair("MongoOptions__Flight__ConnectionString", _mongo.GetConnectionString()),
                                Pair("PostgresOptions__ConnectionString__Flight", _connectionStrings["flight"]),
                                Pair(
                                    "PersistMessageOptions__ConnectionString__Flight",
                                    _connectionStrings["flight_persist"]
                                ),
                            ]
                        )
                ),
                StartAndWaitAsync(
                    "passenger",
                    "Passenger.Host",
                    _passengerPort,
                    HostEnvironment(passengerUrl, identityUrl)
                        .Concat(
                            [
                                Pair("Kestrel__Endpoints__Grpc__Url", grpcPassengerUrl),
                                Pair("MongoOptions__Passenger__DatabaseName", "passenger_service_read"),
                                Pair("MongoOptions__Passenger__ConnectionString", _mongo.GetConnectionString()),
                                Pair("PostgresOptions__ConnectionString__Passenger", _connectionStrings["passenger"]),
                                Pair(
                                    "PersistMessageOptions__ConnectionString__Passenger",
                                    _connectionStrings["passenger_persist"]
                                ),
                            ]
                        )
                )
            );
            await StartAndWaitAsync(
                "booking",
                "Booking.Host",
                _bookingPort,
                HostEnvironment(bookingUrl, identityUrl)
                    .Concat(
                        [
                            Pair("MongoOptions__Booking__DatabaseName", "booking_service_read"),
                            Pair("MongoOptions__Booking__ConnectionString", _mongo.GetConnectionString()),
                            Pair(
                                "PersistMessageOptions__ConnectionString__Booking",
                                _connectionStrings["booking_persist"]
                            ),
                            Pair("Grpc__FlightAddress", grpcFlightUrl),
                            Pair("Grpc__PassengerAddress", grpcPassengerUrl),
                            Pair("Modules__Booking__BackgroundProcessingEnabled", "true"),
                            Pair("EventStoreOptions__ConnectionString", _eventStore.GetConnectionString()),
                        ]
                    )
            );
            await StartAndWaitAsync(
                "gateway",
                "Gateway",
                _gatewayPort,
                GatewayEnvironment(identityUrl, flightUrl, passengerUrl, bookingUrl)
            );
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        for (var index = _hosts.Count - 1; index >= 0; index--)
            await _hosts[index].StopAsync();
        _hosts.Clear();

        await Task.WhenAll(
            IgnoreFailure(_eventStore.DisposeAsync().AsTask()),
            IgnoreFailure(_rabbit.DisposeAsync().AsTask()),
            IgnoreFailure(_mongo.DisposeAsync().AsTask()),
            IgnoreFailure(_postgres.DisposeAsync().AsTask())
        );
    }

    public HttpClient CreateGatewayClient(string? token = null)
    {
        var client = new HttpClient { BaseAddress = new Uri(GatewayUrl), Timeout = TimeSpan.FromSeconds(15) };
        if (token is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task<string> GetAccessTokenAsync()
    {
        using var client = CreateGatewayClient();
        using var response = await client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(
                [
                    new("grant_type", "password"),
                    new("client_id", "client"),
                    new("client_secret", "secret"),
                    new("username", "samh"),
                    new("password", "Admin@123456"),
                    new("scope", "booking-modular-monolith"),
                ]
            )
        );
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("access_token").GetString()!;
    }

    public async Task<RegisteredPassenger> RegisterPassengerAsync()
    {
        var passport = $"A{Guid.NewGuid():N}"[..10];
        var userName = $"ab504-{Guid.NewGuid():N}"[..20];
        var payload = new
        {
            username = userName,
            email = $"{userName}@example.test",
            password = "Admin@123456",
            confirmPassword = "Admin@123456",
            firstName = "Contract",
            lastName = "Test",
            passportNumber = passport,
        };
        var token = await GetAccessTokenAsync();
        using var client = CreateGatewayClient(token);
        using var response = await client.PostAsJsonAsync("/api/v1/identity/register-user", payload, WebJson);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var id = JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
        return new RegisteredPassenger(id, passport, "Contract Test", token);
    }

    public async Task<Guid?> FindPassengerIdAsync(string passport)
    {
        await using var connection = new NpgsqlConnection(_connectionStrings["passenger"]);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select id from passenger where passport_number = @passport_number limit 1",
            connection
        );
        command.Parameters.AddWithValue("passport_number", passport);
        var result = await command.ExecuteScalarAsync();
        return result is Guid id ? id : null;
    }

    public async Task<bool> HasPassengerInboxReceiptAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionStrings["passenger_persist"]);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            select exists (
                select 1 from inbox_message
                where consumer like '%passenger-service-register-new-user-handler%'
            )
            """,
            connection
        );
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    public async Task<bool> HasProcessedUserCreatedOutboxAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionStrings["identity_persist"]);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            select exists (
                select 1 from persist_message
                where data_type like '%UserCreated%'
                  and delivery_type = 1
                  and message_status = 2
            )
            """,
            connection
        );
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private Dictionary<string, string> CommonEnvironment(string serviceUrl, string identityUrl)
    {
        var values = new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["DOTNET_ENVIRONMENT"] = "Development",
            ["Kestrel__Endpoints__Http__Url"] = serviceUrl,
            ["Jwt__Authority"] = GatewayUrl,
            ["Jwt__MetadataAddress"] = $"{identityUrl}/.well-known/openid-configuration",
            ["ObservabilityOptions__UseOTLPExporter"] = "false",
            ["ObservabilityOptions__UseAspireOTLPExporter"] = "false",
            ["Messaging__Transport"] = "RabbitMq",
            ["RabbitMqOptions__HostName"] = "127.0.0.1",
            ["RabbitMqOptions__Port"] = _rabbit.GetMappedPublicPort(5672).ToString(),
            ["RabbitMqOptions__UserName"] = RabbitUser,
            ["RabbitMqOptions__Password"] = RabbitPassword,
            ["PersistMessageOptions__Interval"] = "1",
            ["MongoOptions__ConnectionString"] = _mongo.GetConnectionString(),
            ["EventStoreOptions__ConnectionString"] = _eventStore.GetConnectionString(),
        };
        return values;
    }

    private Dictionary<string, string> GatewayEnvironment(
        string identityUrl,
        string flightUrl,
        string passengerUrl,
        string bookingUrl
    )
    {
        var values = CommonEnvironment(GatewayUrl, identityUrl);
        values["AuthOptions__IssuerUri"] = GatewayUrl;
        values["ReverseProxy__Clusters__identity__Destinations__primary__Address"] = identityUrl;
        values["ReverseProxy__Clusters__flight__Destinations__primary__Address"] = flightUrl;
        values["ReverseProxy__Clusters__passenger__Destinations__primary__Address"] = passengerUrl;
        values["ReverseProxy__Clusters__booking__Destinations__primary__Address"] = bookingUrl;
        values.Remove("Kestrel__Endpoints__Grpc__Url");
        return values;
    }

    private Dictionary<string, string> HostEnvironment(string serviceUrl, string identityUrl)
    {
        var values = CommonEnvironment(serviceUrl, identityUrl);
        values.Remove("Kestrel__Endpoints__Grpc__Url");
        return values;
    }

    private async Task StartAndWaitAsync(
        string serviceName,
        string projectName,
        int httpPort,
        IEnumerable<KeyValuePair<string, string>> environment
    )
    {
        var dll = Path.Combine(
            _repositoryRoot,
            "src",
            projectName == "Gateway" ? "Gateway" : Path.Combine("Services", projectName),
            "bin",
            _buildConfiguration,
            "net10.0",
            $"{projectName}.dll"
        );
        File.Exists(dll).Should().BeTrue($"built host assembly should exist: {dll}");
        var host = new HostProcess(serviceName, dll, _logsDirectory, environment);
        _hosts.Add(host);
        await host.StartAsync();
        await WaitForReadyAsync(serviceName, httpPort, host);
    }

    private async Task WaitForReadyAsync(string serviceName, int httpPort, HostProcess host)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var url = $"http://127.0.0.1:{httpPort}/health/ready";
        Exception? lastException = null;
        while (!timeout.IsCancellationRequested)
        {
            if (host.HasExited)
                throw new InvalidOperationException($"{serviceName} exited before readiness.\n{host.LogTail()}");
            try
            {
                using var response = await client.GetAsync(url, timeout.Token);
                if (response.StatusCode == HttpStatusCode.OK)
                    return;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                lastException = exception;
            }
            await Task.Delay(500);
        }

        throw new TimeoutException(
            $"Timed out waiting for {serviceName} at {url}. Last error: {lastException}\n{host.LogTail()}"
        );
    }

    private async Task InitializePostgresAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var resource = Assembly
            .GetExecutingAssembly()
            .GetManifestResourceStream("CrossService.EndToEnd.Test.01-service-databases.sql")!;
        using var reader = new StreamReader(resource);
        var sql = await reader.ReadToEndAsync();
        foreach (
            var statement in sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        )
        {
            await using var command = new NpgsqlCommand(statement, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    private void ConfigureDatabaseConnections()
    {
        foreach (var name in new[] { "identity", "flight", "passenger", "booking" })
            _connectionStrings[$"{name}_persist"] = BuildConnectionString($"{name}_service_persist_message");
        foreach (var name in new[] { "identity", "flight", "passenger" })
            _connectionStrings[name] = BuildConnectionString($"{name}_service");
    }

    private string BuildConnectionString(string database)
    {
        var user = database.Split('_')[0];
        var builder = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Database = database,
            Username = $"{user}_service",
            Password = $"{user}_service",
        };
        return builder.ConnectionString;
    }

    private static string GetMetadata(Assembly assembly, string key) =>
        assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(attribute => attribute.Key == key).Value!;

    private static string Image(string variable, string fallback) =>
        Environment.GetEnvironmentVariable(variable) ?? fallback;

    private static KeyValuePair<string, string> Pair(string key, string value) => new(key, value);

    private static int FreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task IgnoreFailure(Task task)
    {
        try
        {
            await task;
        }
        catch
        {
            // Containers may not have started if initialization failed.
        }
    }

    public async Task WaitForAsync(Func<Task<bool>> condition, TimeSpan timeout, string description)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (await condition())
                return;
            await Task.Delay(500);
        }
        throw new TimeoutException($"Timed out waiting for {description}.");
    }

    public sealed record RegisteredPassenger(Guid UserId, string PassportNumber, string Name, string AccessToken);

    private sealed class HostProcess : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly StreamWriter _log;
        private readonly object _logLock = new();
        private bool _started;

        public HostProcess(
            string serviceName,
            string dll,
            string logsDirectory,
            IEnumerable<KeyValuePair<string, string>> environment
        )
        {
            LogPath = Path.Combine(logsDirectory, $"{serviceName}.log");
            _log = new StreamWriter(LogPath, append: false) { AutoFlush = true };
            var startInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = Path.GetDirectoryName(dll)!,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(dll);
            foreach (var (key, value) in environment)
                startInfo.Environment[key] = value;
            _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            _process.OutputDataReceived += (_, eventArgs) => WriteLog(eventArgs.Data);
            _process.ErrorDataReceived += (_, eventArgs) => WriteLog(eventArgs.Data);
        }

        public string LogPath { get; }
        public bool HasExited => _process.HasExited;

        public Task StartAsync()
        {
            _process.Start().Should().BeTrue();
            _started = true;
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            return Task.CompletedTask;
        }

        public string LogTail()
        {
            lock (_logLock)
            {
                _log.Flush();
                return string.Join(Environment.NewLine, File.ReadLines(LogPath).TakeLast(50));
            }
        }

        public async Task StopAsync()
        {
            if (!_started)
            {
                _process.Dispose();
                _log.Dispose();
                return;
            }
            if (!_process.HasExited)
            {
                using var terminate = Process.Start(
                    new ProcessStartInfo("kill")
                    {
                        ArgumentList = { "-TERM", _process.Id.ToString() },
                        UseShellExecute = false,
                    }
                );
                try
                {
                    await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                }
                catch (TimeoutException)
                {
                    using var kill = Process.Start(
                        new ProcessStartInfo("kill")
                        {
                            ArgumentList = { "-KILL", _process.Id.ToString() },
                            UseShellExecute = false,
                        }
                    );
                    await _process.WaitForExitAsync();
                }
            }
            _process.Dispose();
            _log.Dispose();
        }

        public ValueTask DisposeAsync() => new(StopAsync());

        private void WriteLog(string? line)
        {
            if (line is null)
                return;
            lock (_logLock)
                _log.WriteLine(line);
        }
    }
}
