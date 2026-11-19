using Api;
using Booking.Data;
using Booking.Extensions.Infrastructure;
using BookingFlight;
using BookingPassenger;
using BuildingBlocks.Jwt;
using BuildingBlocks.TestBase;
using Flight.Data;
using Flight.Flights.Models;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using MongoDB.Driver;
using Passenger.Data;
using Passenger.Passengers.Models;
using Xunit;

namespace Integration.Test.Booking.Infrastructure;

public class GrpcAuthenticationFlowTests : BookingIntegrationTestBase
{
    public GrpcAuthenticationFlowTests(TestReadFixture<Program, BookingReadDbContext> fixture)
        : base(fixture) { }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task registered_booking_clients_should_authenticate_with_real_grpc_services(bool authenticated)
    {
        var id = Guid.NewGuid();
        using var scope = Fixture.ServiceProvider.CreateScope();
        await scope
            .ServiceProvider.GetRequiredService<FlightReadDbContext>()
            .Flight.InsertOneAsync(
                new FlightReadModel
                {
                    Id = id,
                    FlightId = id,
                    FlightNumber = "AUTH123",
                    AircraftId = Guid.NewGuid(),
                    DepartureDate = DateTime.UtcNow,
                    DepartureAirportId = Guid.NewGuid(),
                    ArriveDate = DateTime.UtcNow.AddHours(1),
                    ArriveAirportId = Guid.NewGuid(),
                    DurationMinutes = 60,
                    FlightDate = DateTime.UtcNow,
                    Status = global::Flight.Flights.Enums.FlightStatus.Flying,
                    Price = 100,
                    IsDeleted = false,
                }
            );
        await scope
            .ServiceProvider.GetRequiredService<PassengerReadDbContext>()
            .Passenger.InsertOneAsync(
                new PassengerReadModel
                {
                    Id = id,
                    PassengerId = id,
                    Name = "Authentication Test",
                    PassportNumber = "AUTH123",
                    PassengerType = global::Passenger.Passengers.Enums.PassengerType.Male,
                    Age = 30,
                    IsDeleted = false,
                }
            );

        using var inbound = Fixture.HttpClient;
        inbound.DefaultRequestHeaders.Authorization!.Scheme.Should().Be("FakeBearer");
        var context = new DefaultHttpContext();
        if (authenticated)
        {
            context.Request.Headers.Authorization = inbound.DefaultRequestHeaders.Authorization.ToString();
        }

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Grpc:FlightAddress"] = "http://localhost",
                        ["Grpc:PassengerAddress"] = "http://localhost",
                    }
                )
                .Build()
        );
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddTransient<AuthHeaderHandler>();
        services.AddGrpcClients();
        services.ConfigureAll<HttpClientFactoryOptions>(options =>
            options.HttpMessageHandlerBuilderActions.Add(builder =>
                builder.PrimaryHandler = new TestServerHandler(Fixture.HttpClient)
            )
        );
        await using var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        var previousContext = accessor.HttpContext;
        accessor.HttpContext = context;
        try
        {
            var flight = provider.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>();
            var passenger = provider.GetRequiredService<PassengerGrpcService.PassengerGrpcServiceClient>();
            var getFlight = async () =>
                await flight.GetByIdAsync(new BookingFlight.GetByIdRequest { Id = id.ToString() });
            var getPassenger = async () =>
                await passenger.GetByIdAsync(new BookingPassenger.GetByIdRequest { Id = id.ToString() });

            if (authenticated)
            {
                (await getFlight()).FlightDto.Id.Should().Be(id.ToString());
                (await getPassenger()).PassengerDto.Id.Should().Be(id.ToString());
            }
            else
            {
                await getFlight
                    .Should()
                    .ThrowAsync<RpcException>()
                    .Where(exception => exception.StatusCode == StatusCode.Unauthenticated);
                await getPassenger
                    .Should()
                    .ThrowAsync<RpcException>()
                    .Where(exception => exception.StatusCode == StatusCode.Unauthenticated);
            }
        }
        finally
        {
            accessor.HttpContext = previousContext;
        }
    }

    private sealed class TestServerHandler : HttpMessageHandler
    {
        private readonly HttpClient _client;

        public TestServerHandler(HttpClient client)
        {
            _client = client;
            _client.DefaultRequestHeaders.Clear();
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            request.RequestUri = new Uri(_client.BaseAddress!, request.RequestUri!.PathAndQuery);
            return _client.SendAsync(request, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _client.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
