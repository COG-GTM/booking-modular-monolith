using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Api;
using Booking.Data;
using BookingFlight;
using BookingPassenger;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.EventStoreDB.Events;
using BuildingBlocks.EventStoreDB.Serialization;
using BuildingBlocks.EventStoreDB.Subscriptions;
using BuildingBlocks.TestBase;
using FluentAssertions;
using EventStore.Client;
using global::Booking.Booking.Features.CreatingBook.V1;
using global::Booking.Booking.Models;
using global::Booking.Booking.ValueObjects;
using Grpc.Core;
using Grpc.Core.Testing;
using Integration.Test.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Driver;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Xunit;
using GetByIdRequest = BookingFlight.GetByIdRequest;

namespace Integration.Test.Booking.Features
{
    public class CreateBookingTests : BookingIntegrationTestBase
    {
        public CreateBookingTests(TestReadFixture<Program, BookingReadDbContext> integrationTestFixture) : base(
            integrationTestFixture)
        {
        }

        protected override void RegisterTestsServices(IServiceCollection services)
        {
            MockFlightGrpcServices(services);
            MockPassengerGrpcServices(services);
        }

        [Fact]
        public async Task should_create_booking_to_event_store_currectly()
        {
            // Arrange
            var command = new FakeCreateBookingCommand().Generate();

            // Act
            var response = await Fixture.SendAsync(command);

            // Assert
            response?.Id.Should().BeGreaterThanOrEqualTo(0);

            (await Fixture.WaitForPublishing<BookingCreated>()).Should().Be(true);
        }

        [Fact]
        public async Task should_project_booking_after_poison_events_in_event_store()
        {
            // Arrange
            var eventStoreClient = Fixture.ServiceProvider.GetRequiredService<EventStoreClient>();
            var poisonStream = $"poison-{Guid.NewGuid():N}";

            // Body that is not valid JSON for a type the subscription resolves
            var invalidJson = new EventData(Uuid.NewUuid(), EventTypeMapper.ToName<BookingCreatedDomainEvent>(),
                Encoding.UTF8.GetBytes("not json"));

            // Resolvable type that is not an INotification, so it can't be projected
            var notNotification = new EventData(Uuid.NewUuid(), "System_Object", Encoding.UTF8.GetBytes("{}"));

            // A valid booking event appended after the poison events, in the same stream so ordering is guaranteed
            var validBooking = new BookingCreatedDomainEvent(Guid.NewGuid(), PassengerInfo.Of("Alice"),
                Trip.Of("FL-1", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(1), 100,
                    $"poison-regression-{Guid.NewGuid():N}", "12A"));

            var bookingsBefore = await Fixture.ExecuteReadContextAsync(db =>
                db.Booking.CountDocumentsAsync(FilterDefinition<BookingReadModel>.Empty));

            // Act
            var poisonWrite = await eventStoreClient.AppendToStreamAsync(poisonStream, StreamState.NoStream,
                new[] { invalidJson, notNotification, validBooking.ToJsonEventData() });

            // Assert
            (await WaitUntil(async () => await Fixture.ExecuteReadContextAsync(async db =>
                await db.Booking.CountDocumentsAsync(FilterDefinition<BookingReadModel>.Empty) > bookingsBefore)))
                .Should().BeTrue();

            var checkpointRepository = Fixture.ServiceProvider.GetRequiredService<ISubscriptionCheckpointRepository>();
            var checkpoint = await checkpointRepository.Load("default", default);
            checkpoint.Should().BeGreaterThan(poisonWrite.LogPosition.CommitPosition);

            var deadLetters = await eventStoreClient
                .ReadStreamAsync(Direction.Forwards, EventStoreDBSubscriptionToAll.GetDeadLetterStreamName("default"),
                    StreamPosition.Start)
                .ToListAsync();

            var deadLetteredIds = deadLetters
                .Select(e => JObject.Parse(Encoding.UTF8.GetString(e.Event.Metadata.Span))["OriginalEventId"]?.ToString())
                .ToList();

            deadLetteredIds.Should().Contain(invalidJson.EventId.ToString())
                .And.Contain(notNotification.EventId.ToString());
        }

        private static async Task<bool> WaitUntil(Func<Task<bool>> condition, int timeoutSeconds = 90)
        {
            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

            while (DateTime.UtcNow < deadline)
            {
                if (await condition())
                    return true;

                await Task.Delay(250);
            }

            return await condition();
        }


        private void MockPassengerGrpcServices(IServiceCollection services)
        {
            services.Replace(ServiceDescriptor.Singleton(x =>
            {
                var mockPassenger = Substitute.For<PassengerGrpcService.PassengerGrpcServiceClient>();

                mockPassenger.GetByIdAsync(Arg.Any<BookingPassenger.GetByIdRequest>())
                    .Returns(TestCalls.AsyncUnaryCall(Task.FromResult(FakePassengerResponse.Generate()),
                        Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

                return mockPassenger;
            }));
        }

        private void MockFlightGrpcServices(IServiceCollection services)
        {
            services.Replace(ServiceDescriptor.Singleton(x =>
            {
                var mockFlight = Substitute.For<FlightGrpcService.FlightGrpcServiceClient>();

                mockFlight.GetByIdAsync(Arg.Any<GetByIdRequest>())
                    .Returns(TestCalls.AsyncUnaryCall(Task.FromResult(FakeFlightResponse.Generate()),
                        Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

                mockFlight.GetAvailableSeatsAsync(Arg.Any<GetAvailableSeatsRequest>())
                    .Returns(TestCalls.AsyncUnaryCall(Task.FromResult(FakeGetAvailableSeatsResponse.Generate()),
                        Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

                mockFlight.ReserveSeatAsync(Arg.Any<ReserveSeatRequest>())
                    .Returns(TestCalls.AsyncUnaryCall(Task.FromResult(FakeReserveSeatResponse.Generate()),
                        Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

                return mockFlight;
            }));
        }
    }
}