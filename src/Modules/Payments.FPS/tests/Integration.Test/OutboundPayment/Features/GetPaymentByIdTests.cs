using FluentAssertions;
using Integration.Test.Fakes;
using Payments.FPS.OutboundPayments.Features.GettingPaymentById.V1;
using Xunit;

namespace Integration.Test.OutboundPayment.Features;

public class GetPaymentByIdTests : PaymentsIntegrationTestBase
{
    public GetPaymentByIdTests(
        BuildingBlocks.TestBase.TestFixture<
            Api.Program,
            Payments.FPS.Data.PaymentsDbContext,
            Payments.FPS.Data.PaymentsReadDbContext
        > fixture
    )
        : base(fixture) { }

    [Fact]
    public async Task should_get_payment_by_id()
    {
        var command = new FakeCreateOutboundPaymentMongoCommand().Generate();
        await Fixture.SendAsync(command);
        var response = await Fixture.SendAsync(new GetPaymentById(command.Id));
        response.OutboundPaymentDto.Id.Should().Be(command.Id);
    }
}
