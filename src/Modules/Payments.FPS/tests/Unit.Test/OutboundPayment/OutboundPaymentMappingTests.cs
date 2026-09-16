using FluentAssertions;
using MapsterMapper;
using Payments.FPS.OutboundPayments.Dtos;
using Payments.FPS.OutboundPayments.Enums;
using Payments.FPS.OutboundPayments.Models;
using Unit.Test.Common;
using Xunit;

namespace Unit.Test.OutboundPayment;

[Collection(nameof(UnitTestFixture))]
public class OutboundPaymentMappingTests
{
    private readonly IMapper _mapper;

    public OutboundPaymentMappingTests(UnitTestFixture fixture) => _mapper = fixture.Mapper;

    [Fact]
    public void supports_read_model_to_dto_mapping()
    {
        var source = new OutboundPaymentReadModel
        {
            Id = Guid.NewGuid(),
            OutboundPaymentId = Guid.NewGuid(),
            Amount = 1,
            Currency = "GBP",
            DebtorSortCode = "040004",
            DebtorAccountNumber = "12345678",
            CreditorSortCode = "202020",
            CreditorAccountNumber = "87654321",
            Reference = "REF",
            Status = PaymentStatus.Initiated,
            IsDeleted = false,
        };
        _mapper.Map<OutboundPaymentDto>(source).Id.Should().Be(source.OutboundPaymentId);
    }
}
