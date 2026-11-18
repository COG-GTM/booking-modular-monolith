using System.Diagnostics;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Booking.Booking.Features.CreatingBook.V1;
using BuildingBlocks.EventStoreDB;
using BuildingBlocks.EventStoreDB.Events;
using BuildingBlocks.EventStoreDB.Subscriptions;
using EventStore.Client;
using FluentAssertions;
using Xunit;

namespace Integration.Test.EventStore;

public class EventStoreSecurityTests
{
    [Fact]
    public void event_type_mapper_should_resolve_known_event_types()
    {
        var name = typeof(BookingCreatedDomainEvent).FullName!.Replace(".", "_", StringComparison.Ordinal);

        EventTypeMapper.ToType(name).Should().Be(typeof(BookingCreatedDomainEvent));
    }

    [Theory]
    [InlineData("System_Diagnostics_Process")]
    [InlineData("System_IO_FileInfo")]
    [InlineData("Process")]
    [InlineData("BookingCreatedDomainEvent")]
    public void event_type_mapper_should_reject_types_that_are_not_full_event_type_names(string eventTypeName)
    {
        EventTypeMapper.ToType(eventTypeName).Should().BeNull();
    }

    [Fact]
    public void only_concrete_event_types_are_allowed()
    {
        EventTypeMapper.IsAllowedEventType(typeof(BookingCreatedDomainEvent)).Should().BeTrue();
        EventTypeMapper.IsAllowedEventType(typeof(Process)).Should().BeFalse();
        EventTypeMapper.IsAllowedEventType(typeof(BuildingBlocks.Core.Event.IEvent)).Should().BeFalse();
    }

    [Fact]
    public void booking_stream_prefix_should_match_booking_stream_ids()
    {
        var id = Guid.NewGuid();

        StreamNameMapper.ToStreamId<global::Booking.Booking.Models.Booking>(id)
            .Should().StartWith(StreamNameMapper.ToStreamPrefix<global::Booking.Booking.Models.Booking>());
    }

    [Fact]
    public void failing_event_should_be_skipped_after_max_attempts()
    {
        var tracker = new EventFailureTracker();
        var eventId = Uuid.NewUuid();

        tracker.RegisterFailure(eventId, 3).Should().BeFalse();
        tracker.RegisterFailure(eventId, 3).Should().BeFalse();
        tracker.RegisterFailure(eventId, 3).Should().BeTrue();
    }

    [Fact]
    public void failure_count_should_restart_for_a_different_event()
    {
        var tracker = new EventFailureTracker();

        tracker.RegisterFailure(Uuid.NewUuid(), 2).Should().BeFalse();
        tracker.RegisterFailure(Uuid.NewUuid(), 2).Should().BeFalse();
        tracker.Attempts.Should().Be(1);
    }

    [Fact]
    public void tls_validator_should_accept_certificate_signed_by_configured_ca()
    {
        using var ca = CreateCa("eventstore-test-ca");
        using var node = CreateNode(ca, "eventstore");

        EventStoreTlsValidator.Validate(node, SslPolicyErrors.RemoteCertificateChainErrors, CaPublicCertificate(ca))
            .Should().BeTrue();
    }

    [Fact]
    public void tls_validator_should_reject_certificate_from_another_ca()
    {
        using var ca = CreateCa("eventstore-test-ca");
        using var otherCa = CreateCa("attacker-ca");
        using var node = CreateNode(otherCa, "eventstore");

        EventStoreTlsValidator.Validate(node, SslPolicyErrors.RemoteCertificateChainErrors, CaPublicCertificate(ca))
            .Should().BeFalse();
    }

    [Fact]
    public void tls_validator_should_reject_host_name_mismatch()
    {
        using var ca = CreateCa("eventstore-test-ca");
        using var node = CreateNode(ca, "eventstore");

        EventStoreTlsValidator.Validate(
                node,
                SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch,
                CaPublicCertificate(ca))
            .Should().BeFalse();
    }

    private static X509Certificate2 CreateCa(string name)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
    }

    private static X509Certificate2 CreateNode(X509Certificate2 ca, string dnsName)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={dnsName}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(dnsName);
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false));

        return request.Create(ca, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(10),
            RandomNumberGenerator.GetBytes(16));
    }

    private static X509Certificate2 CaPublicCertificate(X509Certificate2 ca) =>
        X509Certificate2.CreateFromPem(ca.ExportCertificatePem());
}
