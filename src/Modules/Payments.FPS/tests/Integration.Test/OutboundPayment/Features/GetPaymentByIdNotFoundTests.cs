using BuildingBlocks.Exception;
using FluentAssertions;
using Integration.Test.Fakes;
using MassTransit;
using Payments.FPS.OutboundPayments.Enums;
using Payments.FPS.OutboundPayments.Exceptions;
using Payments.FPS.OutboundPayments.Features.GettingPaymentById.V1;
using Xunit;

namespace Integration.Test.OutboundPayment.Features;

public class GetPaymentByIdNotFoundTests : PaymentsIntegrationTestBase
{
    public GetPaymentByIdNotFoundTests(
        BuildingBlocks.TestBase.TestFixture<
            Api.Program,
            Payments.FPS.Data.PaymentsDbContext,
            Payments.FPS.Data.PaymentsReadDbContext
        > fixture
    )
        : base(fixture) { }

    [Fact]
    public async Task should_return_full_dto_for_existing_payment()
    {
        var command = new FakeCreateOutboundPaymentMongoCommand().Generate();
        await Fixture.SendAsync(command);

        var response = await Fixture.SendAsync(new GetPaymentById(command.Id));

        var dto = response.OutboundPaymentDto;
        dto.Id.Should().Be(command.Id);
        dto.Amount.Should().Be(command.Amount);
        dto.Currency.Should().Be(command.Currency);
        dto.DebtorSortCode.Should().Be(command.DebtorSortCode);
        dto.DebtorAccountNumber.Should().Be(command.DebtorAccountNumber);
        dto.CreditorSortCode.Should().Be(command.CreditorSortCode);
        dto.CreditorAccountNumber.Should().Be(command.CreditorAccountNumber);
        dto.Reference.Should().Be(command.Reference);
        dto.Status.Should().Be(PaymentStatus.Submitted);
        dto.RejectionReason.Should().BeNull();
    }

    [Fact]
    public async Task should_throw_not_found_for_unknown_payment()
    {
        Func<Task> act = () => Fixture.SendAsync(new GetPaymentById(NewId.NextGuid()));

        await act.Should().ThrowAsync<OutboundPaymentNotFoundException>();
    }

    [Fact]
    public async Task should_throw_not_found_for_soft_deleted_payment()
    {
        var command = new FakeCreateOutboundPaymentMongoCommand().Generate() with { IsDeleted = true };
        await Fixture.SendAsync(command);

        Func<Task> act = () => Fixture.SendAsync(new GetPaymentById(command.Id));

        await act.Should().ThrowAsync<OutboundPaymentNotFoundException>();
    }

    [Fact]
    public async Task should_fail_validation_for_empty_id()
    {
        Func<Task> act = () => Fixture.SendAsync(new GetPaymentById(Guid.Empty));

        await act.Should().ThrowAsync<ValidationException>();
    }
}
