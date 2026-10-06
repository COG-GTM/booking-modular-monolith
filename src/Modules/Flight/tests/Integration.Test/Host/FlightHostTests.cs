using System.Net;
using System.Net.Http.Json;
using BuildingBlocks.Core;
using BuildingBlocks.Jwt;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.TestBase;
using BuildingBlocks.Web;
using Contracts.Grpc.Flight.V1;
using Flight;
using Flight.Data;
using Flight.Flights.Exceptions;
using Flight.Host;
using FluentAssertions;
using Grpc.AspNetCore.Server;
using Grpc.Core;
using Grpc.Health.V1;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Integration.Test.Host;

// The standalone Flight.Host must expose the same infrastructure surface as the monolith host
// (root, health, gRPC health, problem details) while only wiring the Flight module and RabbitMQ.
public class FlightHostTests : FlightIntegrationTestBase
{
    public FlightHostTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFactory)
        : base(integrationTestFactory) { }

    [Fact]
    public async Task root_endpoint_should_return_the_flight_service_name()
    {
        var expectedName = Fixture.Configuration["AppOptions:Name"];

        var response = await Fixture.HttpClient.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be(expectedName).And.Be("Flight-Service");
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/alive")]
    public async Task health_endpoints_should_be_mapped_and_healthy(string path)
    {
        var response = await Fixture.HttpClient.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }

    [Fact]
    public async Task grpc_health_service_should_report_serving()
    {
        var healthClient = new Health.HealthClient(Fixture.Channel);

        var response = await healthClient.CheckAsync(new HealthCheckRequest());

        response.Status.Should().Be(HealthCheckResponse.Types.ServingStatus.Serving);
    }

    [Fact]
    public async Task unknown_flight_over_rest_should_be_a_problem_details_response()
    {
        var response = await Fixture.HttpClient.GetAsync($"api/v1.0/flight/{Guid.NewGuid()}");

        // FlightNotFountException is a plain AppException, which UseCustomProblemDetails maps to 400.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType.Should().NotBeNull();
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Title.Should().Be(nameof(FlightNotFountException));
        problem.Detail.Should().Be("Flight not found!");
        problem.Status.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task unknown_flight_over_grpc_should_be_translated_by_the_exception_interceptor()
    {
        var flightGrpcClient = new FlightGrpcService.FlightGrpcServiceClient(Fixture.Channel);
        var request = new GetByIdRequest { Id = Guid.NewGuid().ToString() };

        var act = async () => await flightGrpcClient.GetByIdAsync(request).ResponseAsync;

        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.Internal);
        exception.Which.Status.Detail.Should().Be("Flight not found!");
    }

    [Fact]
    public void grpc_server_should_use_the_exception_interceptor()
    {
        var grpcOptions = Fixture.ServiceProvider.GetRequiredService<IOptions<GrpcServiceOptions>>().Value;

        grpcOptions.Interceptors.Should().Contain(x => x.Type == typeof(GrpcExceptionInterceptor));
    }

    [Fact]
    public void should_not_register_the_monolith_composite_event_mapper()
    {
        using var scope = Fixture.ServiceProvider.CreateScope();

        scope.ServiceProvider.GetRequiredService<FlightEventMapper>().Should().NotBeNull();
        scope.ServiceProvider.GetServices<IEventMapper>().Should().AllBeOfType<FlightEventMapper>();
    }

    [Fact]
    public void should_host_only_the_flight_module_outbox_and_inbox_store()
    {
        Fixture
            .PersistMessageDbContextTypes.Should()
            .ContainSingle()
            .Which.Should()
            .Be(typeof(IPersistMessageDbContext<FlightRoot>));
        Fixture
            .PersistMessageBackgroundServiceTypes.Should()
            .ContainSingle()
            .Which.Should()
            .Be(typeof(PersistMessageBackgroundService<FlightRoot>));

        using var scope = Fixture.ServiceProvider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IPersistMessageDbContext<FlightRoot>>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<FlightDbContext>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<FlightReadDbContext>().Should().NotBeNull();
    }

    [Fact]
    public void bus_should_use_the_rabbitmq_transport()
    {
        var bus = Fixture.ServiceProvider.GetRequiredService<IBus>();

        bus.Address.Scheme.Should().Be("rabbitmq");
    }

    [Fact]
    public void should_register_jwt_support_services()
    {
        using var scope = Fixture.ServiceProvider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>().Should().BeOfType<CurrentUserProvider>();
        scope.ServiceProvider.GetRequiredService<AuthHeaderHandler>().Should().NotBeNull();
    }
}
