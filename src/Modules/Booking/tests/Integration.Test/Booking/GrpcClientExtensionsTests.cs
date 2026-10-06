using Api;
using Booking.Data;
using BookingFlight;
using BookingPassenger;
using BuildingBlocks.Jwt;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Integration.Test.Booking;

public class GrpcClientExtensionsTests : BookingIntegrationTestBase
{
    public GrpcClientExtensionsTests(TestReadFixture<Program, BookingReadDbContext> integrationTestFixture)
        : base(integrationTestFixture) { }

    [Theory]
    [InlineData(nameof(FlightGrpcService.FlightGrpcServiceClient))]
    [InlineData(nameof(PassengerGrpcService.PassengerGrpcServiceClient))]
    public void should_forward_auth_header_in_grpc_client_pipeline(string clientName)
    {
        // Arrange
        var handlerFactory = Fixture.ServiceProvider.GetRequiredService<IHttpMessageHandlerFactory>();

        // Act
        var handler = handlerFactory.CreateHandler(clientName);

        // Assert
        HandlerChain(handler).Should().ContainSingle(h => h is AuthHeaderHandler);
    }

    private static IEnumerable<HttpMessageHandler> HandlerChain(HttpMessageHandler handler)
    {
        var current = handler;
        while (current is not null)
        {
            yield return current;
            current = current is DelegatingHandler delegating ? delegating.InnerHandler : null;
        }
    }
}
