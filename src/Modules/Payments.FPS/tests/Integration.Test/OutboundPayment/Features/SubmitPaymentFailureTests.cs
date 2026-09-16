using BuildingBlocks.Exception;
using FluentAssertions;
using Integration.Test.Fakes;
using Microsoft.EntityFrameworkCore;
using Payments.FPS.OutboundPayments.Exceptions;
using Payments.FPS.OutboundPayments.ValueObjects;
using Xunit;

namespace Integration.Test.OutboundPayment.Features;

public class SubmitPaymentFailureTests : PaymentsIntegrationTestBase
{
    public SubmitPaymentFailureTests(
        BuildingBlocks.TestBase.TestFixture<
            Api.Program,
            Payments.FPS.Data.PaymentsDbContext,
            Payments.FPS.Data.PaymentsReadDbContext
        > fixture
    )
        : base(fixture) { }

    [Fact]
    public async Task should_persist_all_fields_of_submitted_payment()
    {
        var command = new FakeSubmitPaymentCommand().Generate();

        await Fixture.SendAsync(command);

        var payment = await Fixture.ExecuteDbContextAsync(db =>
            db.OutboundPayments.FindAsync(OutboundPaymentId.Of(command.Id))
        );
        payment.Should().NotBeNull();
        payment!.Amount.Value.Should().Be(command.Amount);
        payment.Amount.Currency.Should().Be("GBP");
        payment.DebtorAccount.Should().Be(UkAccount.Of(command.DebtorSortCode, command.DebtorAccountNumber));
        payment.CreditorAccount.Should().Be(UkAccount.Of(command.CreditorSortCode, command.CreditorAccountNumber));
        payment.Reference.Should().Be(command.Reference);
        payment.RejectionReason.Should().BeNull();
        payment.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task should_throw_when_submitting_same_payment_id_twice()
    {
        var command = new FakeSubmitPaymentCommand().Generate();
        await Fixture.SendAsync(command);

        Func<Task> act = () => Fixture.SendAsync(command with { Reference = "DUPLICATE" });

        await act.Should().ThrowAsync<OutboundPaymentAlreadyExistException>();
        var count = await Fixture.ExecuteDbContextAsync(db =>
            db.OutboundPayments.CountAsync(x => x.Id == OutboundPaymentId.Of(command.Id))
        );
        count.Should().Be(1);
    }

    [Fact]
    public async Task should_fail_validation_and_not_persist_invalid_payment()
    {
        var command = new FakeSubmitPaymentCommand().Generate() with { Amount = 0m, DebtorSortCode = "12" };

        Func<Task> act = () => Fixture.SendAsync(command);

        await act.Should().ThrowAsync<ValidationException>();
        var payment = await Fixture.ExecuteDbContextAsync(db =>
            db.OutboundPayments.FindAsync(OutboundPaymentId.Of(command.Id))
        );
        payment.Should().BeNull();
    }

    [Fact]
    public async Task should_fail_validation_when_reference_is_too_long()
    {
        var command = new FakeSubmitPaymentCommand().Generate() with { Reference = new string('A', 36) };

        Func<Task> act = () => Fixture.SendAsync(command);

        await act.Should().ThrowAsync<ValidationException>();
    }
}
