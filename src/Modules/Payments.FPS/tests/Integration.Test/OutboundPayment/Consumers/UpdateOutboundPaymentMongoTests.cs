using FluentAssertions;
using Integration.Test.Fakes;
using MassTransit;
using MongoDB.Driver;
using Payments.FPS.Consumers.ReceivingSchemeSettlement.V1;
using Payments.FPS.OutboundPayments.Enums;
using Payments.FPS.OutboundPayments.Exceptions;
using Xunit;

namespace Integration.Test.OutboundPayment.Consumers;

public class UpdateOutboundPaymentMongoTests : PaymentsIntegrationTestBase
{
    public UpdateOutboundPaymentMongoTests(
        BuildingBlocks.TestBase.TestFixture<
            Api.Program,
            Payments.FPS.Data.PaymentsDbContext,
            Payments.FPS.Data.PaymentsReadDbContext
        > fixture
    )
        : base(fixture) { }

    [Fact]
    public async Task should_update_status_to_settled_and_keep_other_fields()
    {
        var created = new FakeCreateOutboundPaymentMongoCommand().Generate();
        await Fixture.SendAsync(created);

        await Fixture.SendAsync(new UpdateOutboundPaymentMongo(created.Id, PaymentStatus.Settled, null));

        var readModel = await Fixture.ExecuteReadContextAsync(db =>
            db.OutboundPayment.Find(x => x.OutboundPaymentId == created.Id).SingleAsync()
        );
        readModel.Status.Should().Be(PaymentStatus.Settled);
        readModel.RejectionReason.Should().BeNull();
        readModel.IsDeleted.Should().BeFalse();
        readModel.Amount.Should().Be(created.Amount);
        readModel.Reference.Should().Be(created.Reference);
        readModel.DebtorSortCode.Should().Be(created.DebtorSortCode);
        readModel.CreditorAccountNumber.Should().Be(created.CreditorAccountNumber);
    }

    [Fact]
    public async Task should_update_status_to_rejected_with_reason()
    {
        var created = new FakeCreateOutboundPaymentMongoCommand().Generate();
        await Fixture.SendAsync(created);

        await Fixture.SendAsync(new UpdateOutboundPaymentMongo(created.Id, PaymentStatus.Rejected, "AC01"));

        var readModel = await Fixture.ExecuteReadContextAsync(db =>
            db.OutboundPayment.Find(x => x.OutboundPaymentId == created.Id).SingleAsync()
        );
        readModel.Status.Should().Be(PaymentStatus.Rejected);
        readModel.RejectionReason.Should().Be("AC01");
    }

    [Fact]
    public async Task should_soft_delete_read_model_when_is_deleted_is_set()
    {
        var created = new FakeCreateOutboundPaymentMongoCommand().Generate();
        await Fixture.SendAsync(created);

        await Fixture.SendAsync(new UpdateOutboundPaymentMongo(created.Id, created.Status, null, IsDeleted: true));

        var readModel = await Fixture.ExecuteReadContextAsync(db =>
            db.OutboundPayment.Find(x => x.OutboundPaymentId == created.Id).SingleAsync()
        );
        readModel.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task should_throw_when_read_model_does_not_exist()
    {
        Func<Task> act = () =>
            Fixture.SendAsync(new UpdateOutboundPaymentMongo(NewId.NextGuid(), PaymentStatus.Settled, null));

        await act.Should().ThrowAsync<OutboundPaymentNotFoundException>();
    }
}
