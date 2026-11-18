using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Booking.Extensions.Infrastructure;
using BookingFlight;
using BookingPassenger;
using BuildingBlocks.Core;
using BuildingBlocks.EventStoreDB.Repository;
using BuildingBlocks.Exception;
using BuildingBlocks.Jwt;
using BuildingBlocks.Mapster;
using BuildingBlocks.ProblemDetails;
using BuildingBlocks.Web;
using Duende.IdentityServer.EntityFramework.Entities;
using FluentAssertions;
using Grpc.Core;
using Integration.Test.Fakes;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using NSubstitute;
using Xunit;
using BookingAggregate = global::Booking.Booking.Models.Booking;
using CreateBookingEndpoint = global::Booking.Booking.Features.CreatingBook.V1.CreateBookingEndpoint;
using CreateBookingRequestDto = global::Booking.Booking.Features.CreatingBook.V1.CreateBookingRequestDto;

namespace Integration.Test.Booking.Features;

public class CreateBookingReservationConflictTests
{
    private const string TestToken = "booking-integration-token";

    [Theory]
    [InlineData(StatusCode.Aborted, HttpStatusCode.Conflict)]
    [InlineData(StatusCode.AlreadyExists, HttpStatusCode.Conflict)]
    [InlineData(StatusCode.InvalidArgument, HttpStatusCode.BadRequest)]
    public async Task create_booking_should_return_problem_details_when_reservation_fails(
        StatusCode reservationStatus,
        HttpStatusCode expectedStatus
    )
    {
        var downstreamBuilder = WebApplication.CreateBuilder();
        downstreamBuilder.WebHost.UseTestServer();
        downstreamBuilder.Services.AddGrpc(options => options.Interceptors.Add<GrpcExceptionInterceptor>());
        downstreamBuilder.Services.AddSingleton(new FlightStub(reservationStatus));

        await using var downstream = downstreamBuilder.Build();
        downstream.MapGrpcService<FlightStub>();
        downstream.MapGrpcService<PassengerStub>();
        await downstream.StartAsync();

        var repository = Substitute.For<IEventStoreDBRepository<BookingAggregate>>();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Grpc:FlightAddress"] = "http://flight.test";
        builder.Configuration["Grpc:PassengerAddress"] = "http://passenger.test";

        builder.Services.AddProblemDetails();
        builder.Services.AddCustomVersioning();
        builder
            .Services.AddAuthorizationBuilder()
            .AddPolicy(
                nameof(ApiScope),
                policy =>
                    policy.RequireAssertion(context =>
                        context.Resource is HttpContext requestContext
                        && requestContext.Request.Headers.Authorization == $"Bearer {TestToken}"
                    )
            );
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddTransient<AuthHeaderHandler>();
        builder.Services.AddGrpcClients();
        builder.Services.ConfigureAll<HttpClientFactoryOptions>(options =>
            options.HttpMessageHandlerBuilderActions.Add(handlerBuilder =>
                handlerBuilder.PrimaryHandler = downstream.GetTestServer().CreateHandler()
            )
        );
        builder.Services.AddCustomMapster(typeof(CreateBookingEndpoint).Assembly);
        builder.Services.AddMediatR(config => config.RegisterServicesFromAssemblyContaining<CreateBookingEndpoint>());
        builder.Services.AddSingleton(repository);
        builder.Services.AddSingleton(Substitute.For<IEventDispatcher>());
        builder.Services.AddScoped<ICurrentUserProvider, CurrentUserProvider>();

        await using var app = builder.Build();
        app.UseCustomProblemDetails();
        new CreateBookingEndpoint().MapEndpoint(app);
        await app.StartAsync();

        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestToken);
        var command = new FakeCreateBookingCommand().Generate();
        var request = new CreateBookingRequestDto(command.PassengerId, command.FlightId, "Test booking");

        using var response = await client.PostAsJsonAsync("/api/v1/booking", request);

        response.StatusCode.Should().Be(expectedStatus);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Status.Should().Be((int)expectedStatus);
        problem.Detail.Should().Contain("Seat reservation rejected");
        await repository.DidNotReceive().Add(Arg.Any<BookingAggregate>(), Arg.Any<CancellationToken>());
    }

    public sealed class FlightStub(StatusCode reservationStatus) : FlightGrpcService.FlightGrpcServiceBase
    {
        public override Task<GetFlightByIdResult> GetById(
            BookingFlight.GetByIdRequest request,
            ServerCallContext context
        )
        {
            RequireForwardedToken(context);
            return Task.FromResult(FakeFlightResponse.Generate());
        }

        public override Task<GetAvailableSeatsResult> GetAvailableSeats(
            GetAvailableSeatsRequest request,
            ServerCallContext context
        )
        {
            RequireForwardedToken(context);
            return Task.FromResult(FakeGetAvailableSeatsResponse.Generate());
        }

        public override Task<ReserveSeatResult> ReserveSeat(ReserveSeatRequest request, ServerCallContext context)
        {
            RequireForwardedToken(context);
            if (reservationStatus == StatusCode.Aborted)
                throw new DbUpdateConcurrencyException("Seat reservation rejected");

            throw new RpcException(new Status(reservationStatus, "Seat reservation rejected"));
        }
    }

    public sealed class PassengerStub : PassengerGrpcService.PassengerGrpcServiceBase
    {
        public override Task<GetPassengerByIdResult> GetById(
            BookingPassenger.GetByIdRequest request,
            ServerCallContext context
        )
        {
            RequireForwardedToken(context);
            return Task.FromResult(FakePassengerResponse.Generate());
        }
    }

    private static void RequireForwardedToken(ServerCallContext context)
    {
        if (context.RequestHeaders.GetValue("authorization") != $"Bearer {TestToken}")
            throw new RpcException(
                new Status(StatusCode.Unauthenticated, "Booking did not forward the caller's bearer token")
            );
    }
}
