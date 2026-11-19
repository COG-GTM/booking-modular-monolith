using System.Net;
using BuildingBlocks.Grpc;
using Contracts.Grpc.Flight;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace BuildingBlocks.Tests;

public class GrpcClientOptionsBindingTests
{
    [Fact]
    public void binds_built_in_defaults()
    {
        using var provider = CreateServices(new() { ["Grpc:Clients:Flight:Address"] = "https://flight:5001" })
            .BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<GrpcClientOptions>>().Get("Flight");
        options.Address.Should().Be("https://flight:5001");
        options.Resilience.MaxRetryAttempts.Should().Be(3);
        options.Resilience.AttemptTimeout.Should().Be(TimeSpan.FromMinutes(3));
        options.Resilience.TotalTimeout.Should().Be(TimeSpan.FromMinutes(3));
        options.Resilience.CircuitBreaker.SamplingDuration.Should().Be(TimeSpan.FromMinutes(2));
        options.Resilience.CircuitBreaker.FailureRatio.Should().Be(0.1);
        options.Resilience.CircuitBreaker.BreakDuration.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void client_overrides_global_defaults_without_affecting_other_clients()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Grpc:Clients:Flight:Address"] = "https://flight:5001",
                    ["Grpc:Clients:Passenger:Address"] = "https://passenger:5002",
                    ["Grpc:Resilience:MaxRetryAttempts"] = "5",
                    ["Grpc:Resilience:AttemptTimeout"] = "00:00:10",
                    ["Grpc:Resilience:TotalTimeout"] = "00:01:00",
                    ["Grpc:Resilience:CircuitBreaker:SamplingDuration"] = "00:00:30",
                    ["Grpc:Resilience:CircuitBreaker:FailureRatio"] = "0.5",
                    ["Grpc:Resilience:CircuitBreaker:BreakDuration"] = "00:00:20",
                    ["Grpc:Clients:Flight:Resilience:MaxRetryAttempts"] = "2",
                    ["Grpc:Clients:Flight:Resilience:CircuitBreaker:FailureRatio"] = "0.25",
                }
            )
            .Build();
        var services = new ServiceCollection();
        services.AddGrpcServiceClient<FlightGrpcService.FlightGrpcServiceClient>(configuration, "Flight");
        services.AddGrpcServiceClient<Contracts.Grpc.Passenger.PassengerGrpcService.PassengerGrpcServiceClient>(
            configuration,
            "Passenger"
        );
        using var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<IOptionsMonitor<GrpcClientOptions>>();
        var flight = monitor.Get("Flight").Resilience;
        flight.MaxRetryAttempts.Should().Be(2);
        flight.AttemptTimeout.Should().Be(TimeSpan.FromSeconds(10));
        flight.TotalTimeout.Should().Be(TimeSpan.FromMinutes(1));
        flight.CircuitBreaker.SamplingDuration.Should().Be(TimeSpan.FromSeconds(30));
        flight.CircuitBreaker.BreakDuration.Should().Be(TimeSpan.FromSeconds(20));
        flight.CircuitBreaker.FailureRatio.Should().Be(0.25);
        monitor.Get("Passenger").Resilience.MaxRetryAttempts.Should().Be(5);
        monitor.Get("Passenger").Resilience.CircuitBreaker.FailureRatio.Should().Be(0.5);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("flight:5001")]
    [InlineData("/flight")]
    public void invalid_address_fails_startup_with_key(string? address)
    {
        using var provider = CreateServices(new() { ["Grpc:Clients:Flight:Address"] = address }).BuildServiceProvider();
        var start = () => provider.GetRequiredService<IStartupValidator>().Validate();
        start.Should().Throw<OptionsValidationException>().WithMessage("*Grpc:Clients:Flight:Address*");
    }

    [Fact]
    public async Task registered_client_calls_configured_address_using_server_wire_path()
    {
        var services = CreateServices(new() { ["Grpc:Clients:Flight:Address"] = "https://configured-flight:5001" });
        var handler = new RecordingHandler();
        services
            .AddGrpcClient<FlightGrpcService.FlightGrpcServiceClient>()
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IStartupValidator>().Validate();
        var client = provider.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>();
        await client.GetByIdAsync(new GetByIdRequest { Id = Guid.NewGuid().ToString() });
        handler.Address.Should().Be(new Uri("https://configured-flight:5001/flight.FlightGrpcService/GetById"));
        Contracts.Grpc.Passenger.PassengerGrpcService.Descriptor.FullName.Should().Be("passenger.PassengerGrpcService");
    }

    private static IServiceCollection CreateServices(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection().AddGrpcServiceClient<FlightGrpcService.FlightGrpcServiceClient>(
            configuration,
            "Flight"
        );
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public Uri? Address { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Address = request.RequestUri;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Version = HttpVersion.Version20,
                Content = new ByteArrayContent(new byte[5]),
            };
            response.Content.Headers.ContentType = new("application/grpc");
            response.TrailingHeaders.Add("grpc-status", "0");
            return Task.FromResult(response);
        }
    }
}
