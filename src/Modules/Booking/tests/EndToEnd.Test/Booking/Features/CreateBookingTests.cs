using System.Net;
using System.Net.Http.Json;
using Api;
using Booking.Booking.Features.CreatingBook.V1;
using Booking.Data;
using BookingFlight;
using BookingPassenger;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.TestBase;
using EndToEnd.Test.Fakes;
using EndToEnd.Test.Routes;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Xunit;
using GetByIdRequest = BookingFlight.GetByIdRequest;

namespace EndToEnd.Test.Booking.Features;

public class CreateBookingTests : BookingEndToEndTestBase
{
    public CreateBookingTests(TestReadFixture<Program, BookingReadDbContext> integrationTestFixture)
        : base(integrationTestFixture) { }

    protected override void RegisterTestsServices(IServiceCollection services)
    {
        MockFlightGrpcServices(services);
        MockPassengerGrpcServices(services);
    }

    [Fact]
    public async Task should_create_booking_through_http_and_publish_message_to_broker()
    {
        // Arrange
        var request = new FakeCreateBookingRequestDto().Generate();

        // Act
        var result = await Fixture.HttpClient.PostAsJsonAsync(ApiRoutes.Booking.CreateBooking, request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await result.Content.ReadFromJsonAsync<CreateBookingResponseDto>();
        response.Should().NotBeNull();
        response!.Id.Should().BeGreaterThanOrEqualTo(0);

        (await Fixture.WaitForPublishing<BookingCreated>()).Should().Be(true);
    }

    private static void MockPassengerGrpcServices(IServiceCollection services)
    {
        services.Replace(
            ServiceDescriptor.Singleton(_ =>
            {
                var mockPassenger = Substitute.For<PassengerGrpcService.PassengerGrpcServiceClient>();

                mockPassenger
                    .GetByIdAsync(
                        Arg.Any<BookingPassenger.GetByIdRequest>(),
                        Arg.Any<Metadata>(),
                        Arg.Any<DateTime?>(),
                        Arg.Any<CancellationToken>()
                    )
                    .Returns(
                        TestCalls.AsyncUnaryCall(
                            Task.FromResult(FakePassengerResponse.Generate()),
                            Task.FromResult(new Metadata()),
                            () => Status.DefaultSuccess,
                            () => new Metadata(),
                            () => { }
                        )
                    );

                return mockPassenger;
            })
        );
    }

    private static void MockFlightGrpcServices(IServiceCollection services)
    {
        services.Replace(
            ServiceDescriptor.Singleton(_ =>
            {
                var mockFlight = Substitute.For<FlightGrpcService.FlightGrpcServiceClient>();

                mockFlight
                    .GetByIdAsync(
                        Arg.Any<GetByIdRequest>(),
                        Arg.Any<Metadata>(),
                        Arg.Any<DateTime?>(),
                        Arg.Any<CancellationToken>()
                    )
                    .Returns(
                        TestCalls.AsyncUnaryCall(
                            Task.FromResult(FakeFlightResponse.Generate()),
                            Task.FromResult(new Metadata()),
                            () => Status.DefaultSuccess,
                            () => new Metadata(),
                            () => { }
                        )
                    );

                mockFlight
                    .GetAvailableSeatsAsync(
                        Arg.Any<GetAvailableSeatsRequest>(),
                        Arg.Any<Metadata>(),
                        Arg.Any<DateTime?>(),
                        Arg.Any<CancellationToken>()
                    )
                    .Returns(
                        TestCalls.AsyncUnaryCall(
                            Task.FromResult(FakeGetAvailableSeatsResponse.Generate()),
                            Task.FromResult(new Metadata()),
                            () => Status.DefaultSuccess,
                            () => new Metadata(),
                            () => { }
                        )
                    );

                mockFlight
                    .ReserveSeatAsync(
                        Arg.Any<ReserveSeatRequest>(),
                        Arg.Any<Metadata>(),
                        Arg.Any<DateTime?>(),
                        Arg.Any<CancellationToken>()
                    )
                    .Returns(
                        TestCalls.AsyncUnaryCall(
                            Task.FromResult(FakeReserveSeatResponse.Generate()),
                            Task.FromResult(new Metadata()),
                            () => Status.DefaultSuccess,
                            () => new Metadata(),
                            () => { }
                        )
                    );

                return mockFlight;
            })
        );
    }
}
