using FluentAssertions;
using MapsterMapper;
using Payments.FPS.Consumers.ReceivingSchemeSettlement.V1;
using Payments.FPS.OutboundPayments.Dtos;
using Payments.FPS.OutboundPayments.Enums;
using Payments.FPS.OutboundPayments.Features.SubmittingPayment.V1;
using Payments.FPS.OutboundPayments.Models;
using Payments.FPS.OutboundPayments.ValueObjects;
using Unit.Test.Common;
using Xunit;

namespace Unit.Test.OutboundPayment;

[Collection(nameof(UnitTestFixture))]
public class OutboundPaymentMappingConfigurationTests
{
    private readonly IMapper _mapper;

    public OutboundPaymentMappingConfigurationTests(UnitTestFixture fixture) => _mapper = fixture.Mapper;

    private static Payments.FPS.OutboundPayments.Models.OutboundPayment CreatePayment(Guid id)
    {
        var payment = Payments.FPS.OutboundPayments.Models.OutboundPayment.Create(
            OutboundPaymentId.Of(id),
            Amount.Of(125.50m),
            UkAccount.Of("040004", "12345678"),
            UkAccount.Of("202020", "87654321"),
            "INV-1001"
        );
        payment.Submit();
        return payment;
    }

    private static OutboundPaymentReadModel ReadModel(Guid paymentId) =>
        new()
        {
            Id = Guid.NewGuid(),
            OutboundPaymentId = paymentId,
            Amount = 125.50m,
            Currency = "GBP",
            DebtorSortCode = "040004",
            DebtorAccountNumber = "12345678",
            CreditorSortCode = "202020",
            CreditorAccountNumber = "87654321",
            Reference = "INV-1001",
            Status = PaymentStatus.Rejected,
            RejectionReason = "AC01",
            IsDeleted = false,
        };

    [Fact]
    public void maps_aggregate_to_dto_flattening_value_objects()
    {
        var id = Guid.NewGuid();
        var payment = CreatePayment(id);
        payment.Reject("AC01");

        var dto = _mapper.Map<OutboundPaymentDto>(payment);

        dto.Id.Should().Be(id);
        dto.Amount.Should().Be(125.50m);
        dto.Currency.Should().Be("GBP");
        dto.DebtorSortCode.Should().Be("040004");
        dto.DebtorAccountNumber.Should().Be("12345678");
        dto.CreditorSortCode.Should().Be("202020");
        dto.CreditorAccountNumber.Should().Be("87654321");
        dto.Reference.Should().Be("INV-1001");
        dto.Status.Should().Be(PaymentStatus.Rejected);
        dto.RejectionReason.Should().Be("AC01");
    }

    [Fact]
    public void maps_aggregate_to_read_model_with_new_document_id()
    {
        var id = Guid.NewGuid();
        var payment = CreatePayment(id);

        var readModel = _mapper.Map<OutboundPaymentReadModel>(payment);

        readModel.Id.Should().NotBeEmpty();
        readModel.Id.Should().NotBe(id);
        readModel.OutboundPaymentId.Should().Be(id);
        readModel.Amount.Should().Be(125.50m);
        readModel.Currency.Should().Be("GBP");
        readModel.DebtorSortCode.Should().Be("040004");
        readModel.DebtorAccountNumber.Should().Be("12345678");
        readModel.CreditorSortCode.Should().Be("202020");
        readModel.CreditorAccountNumber.Should().Be("87654321");
        readModel.Reference.Should().Be("INV-1001");
        readModel.Status.Should().Be(PaymentStatus.Submitted);
        readModel.RejectionReason.Should().BeNull();
        readModel.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void maps_read_model_to_dto_including_status_and_rejection_reason()
    {
        var paymentId = Guid.NewGuid();
        var source = ReadModel(paymentId);

        var dto = _mapper.Map<OutboundPaymentDto>(source);

        dto.Id.Should().Be(paymentId);
        dto.Amount.Should().Be(125.50m);
        dto.Currency.Should().Be("GBP");
        dto.DebtorSortCode.Should().Be("040004");
        dto.DebtorAccountNumber.Should().Be("12345678");
        dto.CreditorSortCode.Should().Be("202020");
        dto.CreditorAccountNumber.Should().Be("87654321");
        dto.Reference.Should().Be("INV-1001");
        dto.Status.Should().Be(PaymentStatus.Rejected);
        dto.RejectionReason.Should().Be("AC01");
    }

    [Fact]
    public void maps_create_mongo_command_to_read_model_with_new_document_id()
    {
        var id = Guid.NewGuid();
        var command = new CreateOutboundPaymentMongo(
            id,
            125.50m,
            "GBP",
            "040004",
            "12345678",
            "202020",
            "87654321",
            "INV-1001",
            PaymentStatus.Submitted
        );

        var readModel = _mapper.Map<OutboundPaymentReadModel>(command);

        readModel.Id.Should().NotBeEmpty();
        readModel.Id.Should().NotBe(id);
        readModel.OutboundPaymentId.Should().Be(id);
        readModel.Amount.Should().Be(125.50m);
        readModel.Currency.Should().Be("GBP");
        readModel.DebtorSortCode.Should().Be("040004");
        readModel.DebtorAccountNumber.Should().Be("12345678");
        readModel.CreditorSortCode.Should().Be("202020");
        readModel.CreditorAccountNumber.Should().Be("87654321");
        readModel.Reference.Should().Be("INV-1001");
        readModel.Status.Should().Be(PaymentStatus.Submitted);
        readModel.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void maps_update_mongo_command_to_read_model_payment_id()
    {
        var id = Guid.NewGuid();
        var command = new UpdateOutboundPaymentMongo(id, PaymentStatus.Rejected, "AC01");

        var readModel = _mapper.Map<OutboundPaymentReadModel>(command);

        readModel.OutboundPaymentId.Should().Be(id);
        readModel.Status.Should().Be(PaymentStatus.Rejected);
        readModel.RejectionReason.Should().Be("AC01");
    }

    [Fact]
    public void maps_request_dto_to_submit_command_with_generated_id()
    {
        var request = new SubmitPaymentRequestDto(125.50m, "040004", "12345678", "202020", "87654321", "INV-1001");

        var first = _mapper.Map<SubmitPayment>(request);
        var second = _mapper.Map<SubmitPayment>(request);

        first.Id.Should().NotBeEmpty();
        first.Id.Should().NotBe(second.Id);
        first.Amount.Should().Be(125.50m);
        first.DebtorSortCode.Should().Be("040004");
        first.DebtorAccountNumber.Should().Be("12345678");
        first.CreditorSortCode.Should().Be("202020");
        first.CreditorAccountNumber.Should().Be("87654321");
        first.Reference.Should().Be("INV-1001");
    }
}
