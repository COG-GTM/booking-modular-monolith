namespace Unit.Test.OutboundPayment.Features.Handlers.SubmitPayment;

using FluentAssertions;
using Payments.FPS.Consumers.ReceivingSchemeSettlement.V1;
using Payments.FPS.OutboundPayments.Enums;
using Payments.FPS.OutboundPayments.Exceptions;
using Payments.FPS.OutboundPayments.Features.SubmittingPayment.V1;
using Payments.FPS.OutboundPayments.ValueObjects;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

[Collection(nameof(UnitTestFixture))]
public class SubmitPaymentCommandHandlerBehaviorTests
{
    private readonly SubmitPaymentHandler _handler;
    private readonly UnitTestFixture _fixture;

    public SubmitPaymentCommandHandlerBehaviorTests(UnitTestFixture fixture)
    {
        _fixture = fixture;
        _handler = new SubmitPaymentHandler(fixture.DbContext);
    }

    [Fact]
    public async Task handler_returns_command_id_and_persists_all_fields()
    {
        var command = new FakeSubmitPaymentCommand().Generate();

        var response = await _handler.Handle(command, CancellationToken.None);

        response.Id.Should().Be(command.Id);
        var payment = await _fixture.DbContext.OutboundPayments.FindAsync(OutboundPaymentId.Of(command.Id));
        payment.Should().NotBeNull();
        payment!.Amount.Value.Should().Be(command.Amount);
        payment.Amount.Currency.Should().Be("GBP");
        payment.DebtorAccount.SortCode.Should().Be(command.DebtorSortCode);
        payment.DebtorAccount.AccountNumber.Should().Be(command.DebtorAccountNumber);
        payment.CreditorAccount.SortCode.Should().Be(command.CreditorSortCode);
        payment.CreditorAccount.AccountNumber.Should().Be(command.CreditorAccountNumber);
        payment.Reference.Should().Be(command.Reference);
        payment.Status.Should().Be(PaymentStatus.Submitted);
        payment.RejectionReason.Should().BeNull();
        payment.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task handler_raises_initiated_and_submitted_domain_events()
    {
        var command = new FakeSubmitPaymentCommand().Generate();

        await _handler.Handle(command, CancellationToken.None);

        var payment = await _fixture.DbContext.OutboundPayments.FindAsync(OutboundPaymentId.Of(command.Id));
        payment!
            .DomainEvents.Select(x => x.GetType())
            .Should()
            .ContainInOrder(typeof(PaymentInitiatedDomainEvent), typeof(PaymentSubmittedDomainEvent));
        payment
            .DomainEvents.Should()
            .NotContain(x => x is PaymentSettledDomainEvent || x is PaymentRejectedDomainEvent);
    }

    [Fact]
    public async Task duplicate_payment_id_throws_OutboundPaymentAlreadyExistException()
    {
        var command = new FakeSubmitPaymentCommand().Generate();
        await _handler.Handle(command, CancellationToken.None);
        await _fixture.DbContext.SaveChangesAsync();

        Func<Task> act = () => _handler.Handle(command with { Reference = "DUPLICATE" }, CancellationToken.None);

        await act.Should().ThrowAsync<OutboundPaymentAlreadyExistException>();
    }

    [Fact]
    public async Task invalid_amount_throws_InvalidAmountException()
    {
        var command = new FakeSubmitPaymentCommand().Generate() with { Amount = 10.005m };

        Func<Task> act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidAmountException>();
    }

    [Fact]
    public async Task invalid_debtor_account_throws_InvalidUkAccountException()
    {
        var command = new FakeSubmitPaymentCommand().Generate() with { DebtorSortCode = "12-34-56" };

        Func<Task> act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidUkAccountException>();
    }

    [Fact]
    public async Task invalid_creditor_account_throws_InvalidUkAccountException()
    {
        var command = new FakeSubmitPaymentCommand().Generate() with { CreditorAccountNumber = "1234567" };

        Func<Task> act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidUkAccountException>();
    }

    [Fact]
    public async Task empty_id_throws_InvalidOutboundPaymentIdException()
    {
        var command = new FakeSubmitPaymentCommand().Generate() with { Id = Guid.Empty };

        Func<Task> act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOutboundPaymentIdException>();
    }
}
