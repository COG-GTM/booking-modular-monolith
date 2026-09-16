using FluentAssertions;
using Integration.Test.Fakes;
using MongoDB.Driver;
using Payments.FPS.OutboundPayments.Enums;
using Payments.FPS.OutboundPayments.Exceptions;
using Xunit;

namespace Integration.Test.OutboundPayment.Features;

public class CreateOutboundPaymentMongoTests : PaymentsIntegrationTestBase
{
    public CreateOutboundPaymentMongoTests(
        BuildingBlocks.TestBase.TestFixture<
            Api.Program,
            Payments.FPS.Data.PaymentsDbContext,
            Payments.FPS.Data.PaymentsReadDbContext
        > fixture
    )
        : base(fixture) { }

    [Fact]
    public async Task should_insert_read_model_with_all_fields()
    {
        var command = new FakeCreateOutboundPaymentMongoCommand().Generate();

        await Fixture.SendAsync(command);

        var readModel = await Fixture.ExecuteReadContextAsync(db =>
            db.OutboundPayment.Find(x => x.OutboundPaymentId == command.Id).SingleOrDefaultAsync()
        );
        readModel.Should().NotBeNull();
        readModel!.Id.Should().NotBe(command.Id);
        readModel.Amount.Should().Be(command.Amount);
        readModel.Currency.Should().Be(command.Currency);
        readModel.DebtorSortCode.Should().Be(command.DebtorSortCode);
        readModel.DebtorAccountNumber.Should().Be(command.DebtorAccountNumber);
        readModel.CreditorSortCode.Should().Be(command.CreditorSortCode);
        readModel.CreditorAccountNumber.Should().Be(command.CreditorAccountNumber);
        readModel.Reference.Should().Be(command.Reference);
        readModel.Status.Should().Be(PaymentStatus.Submitted);
        readModel.RejectionReason.Should().BeNull();
        readModel.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task should_throw_when_read_model_already_exists()
    {
        var command = new FakeCreateOutboundPaymentMongoCommand().Generate();
        await Fixture.SendAsync(command);

        Func<Task> act = () => Fixture.SendAsync(command);

        await act.Should().ThrowAsync<OutboundPaymentAlreadyExistException>();
        var count = await Fixture.ExecuteReadContextAsync(db =>
            db.OutboundPayment.CountDocumentsAsync(x => x.OutboundPaymentId == command.Id)
        );
        count.Should().Be(1);
    }
}
