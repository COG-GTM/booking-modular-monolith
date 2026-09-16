using System.Net;
using EndToEnd.Test.Routes;
using FluentAssertions;
using Payments.FPS.Data.Seed;
using Xunit;

namespace EndToEnd.Test.OutboundPayment.Features;

public class GetPaymentByIdTests : PaymentsEndToEndTestBase
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
    public async Task get_seeded_payment_returns_ok()
    {
        var id = InitialData.OutboundPayments[0].Id.Value;
        var response = await Fixture.HttpClient.GetAsync(
            ApiRoutes.Payments.GetPaymentById.Replace("{id}", id.ToString(), StringComparison.Ordinal)
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task get_unknown_returns_not_found()
    {
        var response = await Fixture.HttpClient.GetAsync(
            ApiRoutes.Payments.GetPaymentById.Replace("{id}", Guid.NewGuid().ToString(), StringComparison.Ordinal)
        );
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
