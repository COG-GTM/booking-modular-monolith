using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Npgsql;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Testcontainers.MongoDb;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Xunit;

namespace CrossService.EndToEnd.Test;

[Collection(CrossServiceEndToEndCollection.Name)]
public sealed class CrossServiceEndToEndTests(CrossServiceEndToEndFixture fixture)
{
    [Fact]
    public async Task register_user_in_identity_creates_passenger_over_rabbitmq()
    {
        var passenger = await fixture.RegisterPassengerAsync();

        passenger.Name.Should().Be("First Last", fixture.FailureContext());
    }

    [Fact]
    public async Task create_booking_calls_flight_and_passenger_over_grpc()
    {
        var passenger = await fixture.EnsurePassengerAsync();
        var token = await fixture.GetAccessTokenAsync();
        using var client = fixture.CreateGatewayClient(token);

        using var flightResponse = await fixture.WaitForResponseAsync(
            client,
            $"/api/v1.0/flight/{CrossServiceEndToEndFixture.SeededFlightId}"
        );
        flightResponse.StatusCode.Should().Be(HttpStatusCode.OK, fixture.FailureContext());

        using var seatsResponse = await client.GetAsync(
            $"/api/v1.0/flight/get-available-seats/{CrossServiceEndToEndFixture.SeededFlightId}"
        );
        seatsResponse.StatusCode.Should().Be(HttpStatusCode.OK, fixture.FailureContext());
        using var seatsDocument = JsonDocument.Parse(await seatsResponse.Content.ReadAsStringAsync());
        var seatsBefore = seatsDocument.RootElement.GetProperty("seatDtos").GetArrayLength();
        seatsBefore.Should().BeGreaterThan(0, fixture.FailureContext());

        using var bookingResponse = await client.PostAsJsonAsync(
            "/api/v1.0/booking",
            new
            {
                passengerId = passenger.Id,
                flightId = CrossServiceEndToEndFixture.SeededFlightId,
                description = "AB-504 cross-service booking",
            }
        );
        bookingResponse.StatusCode.Should().Be(HttpStatusCode.OK, fixture.FailureContext());

        var seatsDropped = await fixture.WaitUntilAsync(async () =>
        {
            using var response = await client.GetAsync(
                $"/api/v1.0/flight/get-available-seats/{CrossServiceEndToEndFixture.SeededFlightId}"
            );
            if (!response.IsSuccessStatusCode)
                return false;

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.GetProperty("seatDtos").GetArrayLength() == seatsBefore - 1;
        });
        seatsDropped.Should().BeTrue(fixture.FailureContext());

        var bookingProjected = await fixture.WaitUntilAsync(async () =>
        {
            var database = fixture.MongoClient.GetDatabase("booking_service_read");
            var collection = database.GetCollection<BsonDocument>("booking");
            var filter = Builders<BsonDocument>.Filter.Eq("passengerInfo.name", passenger.Name)
                & Builders<BsonDocument>.Filter.Eq("trip.description", "AB-504 cross-service booking");
            var booking = await collection.Find(filter).FirstOrDefaultAsync();
            return booking is not null
                && booking["trip"]["flightNumber"].AsString.Contains("BD467", StringComparison.Ordinal);
        });
        bookingProjected.Should().BeTrue(fixture.FailureContext());
    }

    [Fact]
    public async Task unauthenticated_request_is_rejected_at_gateway()
    {
        using var client = fixture.CreateGatewayClient();
        using var response = await client.GetAsync("/api/v1.0/booking");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, fixture.FailureContext());
    }
}

[CollectionDefinition(Name)]
public sealed class CrossServiceEndToEndCollection : ICollectionFixture<CrossServiceEndToEndFixture>
{
    public const string Name = "Cross-service end-to-end";
}

public sealed class CrossServiceEndToEndFixture : IAsyncLifetime
{
    public static readonly Guid SeededFlightId = Guid.Parse("3c5c0000-97c6-fc34-2eb9-08db322230c9");

    private readonly List<HostProcess> _hosts = [];
    private readonly SemaphoreSlim _passengerLock = new(1, 1);
    private PassengerRegistration? _passenger;
    private PostgreSqlContainer _postgres = null!;
    private MongoDbContainer _mongo = null!;
    private RabbitMqContainer _rabbitMq = null!;
    private IContainer _eventStore = null!;
    private string _root = null!;
    private string _logsDirectory = null!;
    private int _identityPort;
    private int _flightHttpPort;
    private int _flightGrpcPort;
    private int _passengerHttpPort;
    private int _passengerGrpcPort;
    private int _bookingPort;
    private int _gatewayPort;
    private string GatewayAddress => $"http://127.0.0.1:{_gatewayPort}";
    private string IdentityAddress => $"http://127.0.0.1:{_identityPort}";

    public MongoClient MongoClient { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");
        _root = RepositoryRoot();
        _logsDirectory = Path.Combine(AppContext.BaseDirectory, "TestResults", "e2e-logs");
        Directory.CreateDirectory(_logsDirectory);

        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16")
            .WithDatabase("postgres")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithCommand("-c", "wal_level=logical", "-c", "max_prepared_transactions=100")
            .Build();
        _mongo = new MongoDbBuilder().WithImage("mongo:7").Build();
        _rabbitMq = new RabbitMqBuilder()
            .WithImage("rabbitmq:3-management")
            .WithUsername("booking")
            .WithPassword("booking")
            .Build();
        _eventStore = new ContainerBuilder()
            .WithImage("mirror.gcr.io/eventstore/eventstore:23.10.0-bookworm-slim")
            .WithEnvironment("EVENTSTORE_CLUSTER_SIZE", "1")
            .WithEnvironment("EVENTSTORE_RUN_PROJECTIONS", "All")
            .WithEnvironment("EVENTSTORE_START_STANDARD_PROJECTIONS", "True")
            .WithEnvironment("EVENTSTORE_HTTP_PORT", "2113")
            .WithEnvironment("EVENTSTORE_INSECURE", "True")
            .WithEnvironment("EVENTSTORE_ENABLE_ATOM_PUB_OVER_HTTP", "True")
            .WithPortBinding(2113, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(2113))
            .Build();

        await Task.WhenAll(
            _postgres.StartAsync(),
            _mongo.StartAsync(),
            _rabbitMq.StartAsync(),
            _eventStore.StartAsync()
        );
        await CreateServiceDatabasesAsync();
        MongoClient = new MongoClient(_mongo.GetConnectionString());

        _identityPort = FreePort();
        _flightHttpPort = FreePort();
        _flightGrpcPort = FreePort();
        _passengerHttpPort = FreePort();
        _passengerGrpcPort = FreePort();
        _bookingPort = FreePort();
        _gatewayPort = FreePort();

        StartHost("identity", "src/Services/Identity.Host/Identity.Host.csproj", "Identity.Host.dll", BuildIdentityEnvironment());
        await WaitForHostAsync("identity", "/health");
        StartHost("flight", "src/Services/Flight.Host/Flight.Host.csproj", "Flight.Host.dll", BuildServiceEnvironment("Flight"));
        await WaitForHostAsync("flight", "/health/ready");
        StartHost("passenger", "src/Services/Passenger.Host/Passenger.Host.csproj", "Passenger.Host.dll", BuildServiceEnvironment("Passenger"));
        await WaitForHostAsync("passenger", "/health/ready");
        StartHost("booking", "src/Services/Booking.Host/Booking.Host.csproj", "Booking.Host.dll", BuildBookingEnvironment());
        await WaitForHostAsync("booking", "/health/ready");
        StartHost("gateway", "src/Gateway/src/Gateway.csproj", "Gateway.dll", BuildGatewayEnvironment());
        await WaitForHostAsync("gateway", "/.well-known/openid-configuration");
    }

    public async Task DisposeAsync()
    {
        foreach (var host in _hosts.AsEnumerable().Reverse())
        {
            try
            {
                if (!host.Process.HasExited)
                    host.Process.Kill(entireProcessTree: true);
                await host.Process.WaitForExitAsync();
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                host.LogWriter.Dispose();
                host.Process.Dispose();
            }
        }

        _passengerLock.Dispose();
    }

    public HttpClient CreateGatewayClient(string? token = null)
    {
        var client = new HttpClient { BaseAddress = new Uri(GatewayAddress), Timeout = TimeSpan.FromSeconds(30) };
        if (token is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task<PassengerRegistration> RegisterPassengerAsync()
    {
        await _passengerLock.WaitAsync();
        try
        {
            var username = $"ab504-{Guid.NewGuid():N}";
            var passportNumber = $"P{Random.Shared.Next(10_000_000, 99_999_999)}";
            var token = await GetAccessTokenAsync();
            using var client = CreateGatewayClient(token);
            using var response = await client.PostAsJsonAsync(
                "/api/v1.0/identity/register-user",
                new
                {
                    firstName = "First",
                    lastName = "Last",
                    username,
                    email = $"{username}@example.com",
                    password = "Password123",
                    confirmPassword = "Password123",
                    passportNumber,
                }
            );
            response.StatusCode.Should().Be(HttpStatusCode.OK, FailureContext());

            var passengerId = await WaitForPassengerIdAsync(passportNumber);
            var passengerAvailable = await WaitUntilAsync(async () =>
            {
                using var passengerResponse = await client.GetAsync($"/api/v1.0/passenger/{passengerId}");
                if (passengerResponse.StatusCode != HttpStatusCode.OK)
                    return false;

                using var payload = JsonDocument.Parse(await passengerResponse.Content.ReadAsStringAsync());
                return payload.RootElement.GetProperty("passengerDto").GetProperty("name").GetString() == "First Last";
            });
            passengerAvailable.Should().BeTrue(FailureContext());
            (await IdentityOutboxProcessedAsync(passportNumber)).Should().BeTrue(FailureContext());

            _passenger = new PassengerRegistration(passengerId, "First Last");
            return _passenger;
        }
        finally
        {
            _passengerLock.Release();
        }
    }

    public async Task<PassengerRegistration> EnsurePassengerAsync()
    {
        if (_passenger is not null)
            return _passenger;
        return await RegisterPassengerAsync();
    }

    public async Task<string> GetAccessTokenAsync()
    {
        using var client = CreateGatewayClient();
        using var response = await client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["grant_type"] = "password",
                    ["client_id"] = "client",
                    ["client_secret"] = "secret",
                    ["username"] = "samh",
                    ["password"] = "Admin@123456",
                    ["scope"] = "booking-modular-monolith identity-api flight-api passenger-api booking-api",
                }
            )
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK, FailureContext());
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("access_token").GetString()!;
    }

    public string FailureContext()
    {
        var logs = _hosts.Select(host => $"{host.Name} ({host.LogPath}):{Environment.NewLine}{Tail(host.LogPath)}");
        return $"{Environment.NewLine}E2E host logs:{Environment.NewLine}{string.Join(Environment.NewLine, logs)}";
    }

    public async Task<HttpResponseMessage> WaitForResponseAsync(
        HttpClient client,
        string path,
        TimeSpan? timeout = null
    )
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(90));
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var response = await client.GetAsync(path);
                if (response.IsSuccessStatusCode)
                    return response;
                response.Dispose();
            }
            catch (HttpRequestException)
            {
            }

            await Task.Delay(250);
        }

        throw new InvalidOperationException($"Timed out waiting for {path}.{FailureContext()}");
    }

    public async Task<bool> WaitUntilAsync(Func<Task<bool>> predicate, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(90));
        while (DateTime.UtcNow < deadline)
        {
            if (await predicate())
                return true;
            await Task.Delay(250);
        }

        return false;
    }

    private async Task CreateServiceDatabasesAsync()
    {
        var scriptPath = Path.Combine(_root, "deployments", "docker-compose", "init", "postgres", "01-service-databases.sql");
        var script = File.ReadAllLines(scriptPath)
            .Where(line => !line.TrimStart().StartsWith("\\", StringComparison.Ordinal))
            .Aggregate(new StringBuilder(), (builder, line) => builder.AppendLine(line))
            .ToString();
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        foreach (var statement in script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            await using var command = new NpgsqlCommand(statement, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    private void StartHost(string name, string projectPath, string assemblyName, IReadOnlyDictionary<string, string> environment)
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug";
        var projectDirectory = Path.Combine(_root, Path.GetDirectoryName(projectPath)!);
        var outputDirectory = Path.Combine(projectDirectory, "bin", configuration, "net10.0");
        var assemblyPath = Path.Combine(outputDirectory, assemblyName);
        if (!File.Exists(assemblyPath))
            throw new FileNotFoundException($"Expected host output {assemblyPath} was not built.{FailureContext()}");

        var logPath = Path.Combine(_logsDirectory, $"{name}.log");
        var logWriter = new StreamWriter(File.Open(logPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true,
        };
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = outputDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.ArgumentList.Add(assemblyPath);
        foreach (var (key, value) in environment)
            process.StartInfo.Environment[key] = value;
        process.StartInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "E2E";
        process.OutputDataReceived += (_, args) => WriteLogLine(logWriter, args.Data);
        process.ErrorDataReceived += (_, args) => WriteLogLine(logWriter, args.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _hosts.Add(new HostProcess(name, process, logWriter, logPath));
    }

    private async Task WaitForHostAsync(string name, string path)
    {
        var address = name switch
        {
            "identity" => IdentityAddress,
            "flight" => $"http://127.0.0.1:{_flightHttpPort}",
            "passenger" => $"http://127.0.0.1:{_passengerHttpPort}",
            "booking" => $"http://127.0.0.1:{_bookingPort}",
            _ => GatewayAddress,
        };
        using var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(120);
        while (DateTime.UtcNow < deadline)
        {
            var host = _hosts.Single(candidate => candidate.Name == name);
            if (host.Process.HasExited)
                throw new InvalidOperationException($"{name} host exited with code {host.Process.ExitCode}.{FailureContext()}");
            try
            {
                using var response = await client.GetAsync(path);
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }

            await Task.Delay(300);
        }

        throw new InvalidOperationException($"Timed out waiting for {name} {path}.{FailureContext()}");
    }

    private async Task<Guid> WaitForPassengerIdAsync(string passportNumber)
    {
        var connectionString = ServiceConnectionString("passenger");
        Guid? passengerId = null;
        var found = await WaitUntilAsync(async () =>
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "SELECT id FROM passenger WHERE passport_number = @passport_number LIMIT 1",
                connection
            );
            command.Parameters.AddWithValue("passport_number", passportNumber);
            var result = await command.ExecuteScalarAsync();
            if (result is not Guid id)
                return false;
            passengerId = id;
            return true;
        });

        found.Should().BeTrue($"Passenger {passportNumber} should be consumed from RabbitMQ.{FailureContext()}");
        return passengerId!.Value;
    }

    private async Task<bool> IdentityOutboxProcessedAsync(string passportNumber)
    {
        await using var connection = new NpgsqlConnection(ServiceConnectionString("identity"));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM persist_message WHERE data LIKE @passport AND message_status = 'Processed')",
            connection
        );
        command.Parameters.AddWithValue("passport", $"%{passportNumber}%");
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private string ServiceConnectionString(string module)
    {
        var port = _postgres.GetMappedPublicPort(5432);
        return $"Host=127.0.0.1;Port={port};Database={module}_service;Username={module}_service;Password={module}_service;Include Error Detail=true";
    }

    private Dictionary<string, string> BuildIdentityEnvironment()
    {
        var environment = BuildDatabaseAndInfrastructureEnvironment("identity");
        environment["ASPNETCORE_URLS"] = IdentityAddress;
        environment["Urls"] = IdentityAddress;
        environment["AuthOptions__IssuerUri"] = GatewayAddress;
        environment["Jwt__Authority"] = GatewayAddress;
        environment["Jwt__MetadataAddress"] = $"{IdentityAddress}/.well-known/openid-configuration";
        return environment;
    }

    private Dictionary<string, string> BuildServiceEnvironment(string module)
    {
        var key = module.ToLowerInvariant();
        var port = module == "Flight" ? _flightHttpPort : _passengerHttpPort;
        var grpcPort = module == "Flight" ? _flightGrpcPort : _passengerGrpcPort;
        var environment = BuildDatabaseAndInfrastructureEnvironment(key);
        environment["Kestrel__Endpoints__Http__Url"] = $"http://127.0.0.1:{port}";
        environment["Kestrel__Endpoints__Http__Protocols"] = "Http1";
        environment["Kestrel__Endpoints__Grpc__Url"] = $"http://127.0.0.1:{grpcPort}";
        environment["Kestrel__Endpoints__Grpc__Protocols"] = "Http2";
        environment["Jwt__Authority"] = GatewayAddress;
        environment["Jwt__MetadataAddress"] = $"{IdentityAddress}/.well-known/openid-configuration";
        return environment;
    }

    private Dictionary<string, string> BuildBookingEnvironment()
    {
        var environment = BuildDatabaseAndInfrastructureEnvironment("booking");
        environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{_bookingPort}";
        environment["Urls"] = $"http://127.0.0.1:{_bookingPort}";
        environment["Services__flight__grpc__0"] = $"http://127.0.0.1:{_flightGrpcPort}";
        environment["Services__passenger__grpc__0"] = $"http://127.0.0.1:{_passengerGrpcPort}";
        environment["Modules__Booking__SubscriptionId"] = $"e2e-{Guid.NewGuid():N}";
        environment["Jwt__Authority"] = GatewayAddress;
        environment["Jwt__MetadataAddress"] = $"{IdentityAddress}/.well-known/openid-configuration";
        environment["EventStoreOptions__ConnectionString"] = EventStoreConnectionString();
        return environment;
    }

    private Dictionary<string, string> BuildGatewayEnvironment()
    {
        var environment = new Dictionary<string, string>
        {
            ["ASPNETCORE_URLS"] = GatewayAddress,
            ["Urls"] = GatewayAddress,
            ["Jwt__Authority"] = GatewayAddress,
            ["Jwt__MetadataAddress"] = $"{IdentityAddress}/.well-known/openid-configuration",
            ["RateLimiting__Enabled"] = "false",
            ["ReverseProxy__Clusters__identity__Destinations__primary__Address"] = IdentityAddress + "/",
            ["ReverseProxy__Clusters__flight__Destinations__primary__Address"] = $"http://127.0.0.1:{_flightHttpPort}/",
            ["ReverseProxy__Clusters__passenger__Destinations__primary__Address"] = $"http://127.0.0.1:{_passengerHttpPort}/",
            ["ReverseProxy__Clusters__booking__Destinations__primary__Address"] = $"http://127.0.0.1:{_bookingPort}/",
            ["ReverseProxy__Clusters__monolith__Destinations__primary__Address"] = IdentityAddress + "/",
        };
        AddTelemetryEnvironment(environment);
        return environment;
    }

    private Dictionary<string, string> BuildDatabaseAndInfrastructureEnvironment(string module)
    {
        var environment = new Dictionary<string, string>
        {
            [$"PostgresOptions__ConnectionString__{CultureInfoInvariantTitle(module)}"] = ServiceConnectionString(module),
            ["MongoOptions__ConnectionString"] = _mongo.GetConnectionString(),
            ["RabbitMqOptions__HostName"] = _rabbitMq.Hostname,
            ["RabbitMqOptions__Port"] = _rabbitMq.GetMappedPublicPort(5672).ToString(),
            ["RabbitMqOptions__UserName"] = "booking",
            ["RabbitMqOptions__Password"] = "booking",
            ["MessagingOptions__Transport"] = "RabbitMq",
            ["PersistMessageOptions__Interval"] = "1",
        };
        if (module == "flight" || module == "passenger" || module == "booking")
            environment[$"MongoOptions__{CultureInfoInvariantTitle(module)}__DatabaseName"] = $"{module}_service_read";

        AddTelemetryEnvironment(environment);
        return environment;
    }

    private static void AddTelemetryEnvironment(IDictionary<string, string> environment)
    {
        environment["ObservabilityOptions__UseOTLPExporter"] = "false";
        environment["ObservabilityOptions__UseAspireOTLPExporter"] = "false";
        environment["ObservabilityOptions__UsePrometheusExporter"] = "false";
        environment["ObservabilityOptions__UseGrafanaExporter"] = "false";
        environment["ObservabilityOptions__UseJaegerExporter"] = "false";
        environment["ObservabilityOptions__UseZipkinExporter"] = "false";
        environment["ObservabilityOptions__UseConsoleExporter"] = "false";
    }

    private string EventStoreConnectionString() =>
        $"esdb://127.0.0.1:{_eventStore.GetMappedPublicPort(2113)}?tls=false";

    private static string CultureInfoInvariantTitle(string value) =>
        char.ToUpperInvariant(value[0]) + value[1..];

    private static int FreePort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static void WriteLogLine(StreamWriter writer, string? line)
    {
        if (line is null)
            return;
        lock (writer)
            writer.WriteLine(line);
    }

    private static string Tail(string path)
    {
        if (!File.Exists(path))
            return "(log file not created)";
        return string.Join(Environment.NewLine, File.ReadLines(path).TakeLast(80));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "booking-modular-monolith.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed record HostProcess(string Name, Process Process, StreamWriter LogWriter, string LogPath);

    public sealed record PassengerRegistration(Guid Id, string Name);
}
