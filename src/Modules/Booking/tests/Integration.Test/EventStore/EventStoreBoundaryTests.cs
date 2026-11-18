using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Booking.Booking.Features.CreatingBook.V1;
using BuildingBlocks.Core.Event;
using BuildingBlocks.EventStoreDB;
using BuildingBlocks.EventStoreDB.Events;
using BuildingBlocks.EventStoreDB.Subscriptions;
using EventStore.Client;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Integration.Test.EventStore;

public class EventStoreBoundaryTests
{
    [Fact]
    public void reset_should_clear_attempts_and_restart_the_same_event()
    {
        var tracker = new EventFailureTracker();
        var eventId = Uuid.NewUuid();
        tracker.RegisterFailure(eventId, 2).Should().BeFalse();

        tracker.Reset();

        tracker.Attempts.Should().Be(0);
        tracker.RegisterFailure(eventId, 2).Should().BeFalse();
        tracker.Attempts.Should().Be(1);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(-3)]
    public void limits_at_or_below_one_should_skip_on_first_failure(int limit)
    {
        var tracker = new EventFailureTracker();

        tracker.RegisterFailure(Uuid.NewUuid(), limit).Should().BeTrue();
        tracker.Attempts.Should().Be(1);
    }

    [Fact]
    public void returning_to_an_earlier_event_should_not_restore_its_failure_count()
    {
        var tracker = new EventFailureTracker();
        var first = Uuid.NewUuid();
        var second = Uuid.NewUuid();

        tracker.RegisterFailure(first, 2).Should().BeFalse();
        tracker.RegisterFailure(second, 2).Should().BeFalse();
        tracker.RegisterFailure(first, 2).Should().BeFalse();
        tracker.Attempts.Should().Be(1);
    }

    [Theory]
    [InlineData(typeof(BoundaryAbstractEvent))]
    [InlineData(typeof(BoundaryGenericEvent<>))]
    [InlineData(typeof(IBoundaryEvent))]
    [InlineData(typeof(BoundaryValueEvent))]
    public void mapper_should_reject_non_concrete_event_types(Type type)
    {
        EventTypeMapper.IsAllowedEventType(type).Should().BeFalse();
        EventTypeMapper.ToType(type.FullName!.Replace(".", "_", StringComparison.Ordinal)).Should().BeNull();
    }

    [Fact]
    public void mapper_should_return_null_for_unknown_full_name()
    {
        EventTypeMapper
            .ToType("Integration_Test_EventStore_BoundaryMissingEvent_" + Guid.NewGuid().ToString("N"))
            .Should()
            .BeNull();
    }

    [Fact]
    public void mapper_should_round_trip_a_booking_event()
    {
        var name = EventTypeMapper.ToName<BookingCreatedDomainEvent>();

        EventTypeMapper.ToType(name).Should().Be(typeof(BookingCreatedDomainEvent));
    }

    [Fact]
    public void stream_prefix_should_be_the_type_name_followed_by_a_hyphen()
    {
        StreamNameMapper.ToStreamPrefix<global::Booking.Booking.Models.Booking>().Should().Be("Booking-");
        StreamNameMapper.ToStreamPrefix<BoundaryValueEvent>().Should().Be("BoundaryValueEvent-");
    }

    [Fact]
    public void tls_validator_should_reject_an_unavailable_certificate()
    {
        using var ca = CreateCa();

        EventStoreTlsValidator.Validate(null, SslPolicyErrors.RemoteCertificateNotAvailable, ca).Should().BeFalse();
    }

    [Fact]
    public void tls_validator_should_reject_an_expired_certificate_signed_by_configured_ca()
    {
        using var ca = CreateCa();
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=eventstore", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var node = request.Create(
            ca,
            DateTimeOffset.UtcNow.AddDays(-3),
            DateTimeOffset.UtcNow.AddDays(-1),
            RandomNumberGenerator.GetBytes(16)
        );

        EventStoreTlsValidator.Validate(node, SslPolicyErrors.RemoteCertificateChainErrors, ca).Should().BeFalse();
    }

    [Fact]
    public void tls_validator_should_reject_an_untrusted_self_signed_certificate()
    {
        using var ca = CreateCa();
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=eventstore", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var node = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        EventStoreTlsValidator.Validate(node, SslPolicyErrors.RemoteCertificateChainErrors, ca).Should().BeFalse();
    }

    [Fact]
    public void tls_validator_should_accept_a_certificate_without_policy_errors()
    {
        using var ca = CreateCa();

        EventStoreTlsValidator.Validate(ca, SslPolicyErrors.None, ca).Should().BeTrue();
    }

    [Fact]
    public void missing_ca_file_should_fail_when_resolving_client()
    {
        var path = Path.Combine(Path.GetTempPath(), $"eventstore-boundary-{Guid.NewGuid():N}", "missing-ca.crt");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["EventStoreOptions:ConnectionString"] = "esdb://localhost:2113?tls=true",
                    ["EventStoreOptions:TlsCaFile"] = path,
                }
            )
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddEventStoreDB(configuration);
        using var provider = services.BuildServiceProvider();

        Action resolve = () => provider.GetRequiredService<EventStoreClient>();

        resolve.Should().Throw<DirectoryNotFoundException>();
    }

    private static X509Certificate2 CreateCa()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=eventstore-boundary-ca",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow.AddDays(10));
    }

    public abstract class BoundaryAbstractEvent : IEvent;

    public class BoundaryGenericEvent<T> : IEvent;

    public interface IBoundaryEvent : IEvent;

    public struct BoundaryValueEvent : IEvent;
}
