using System.Net;
using System.Net.Http.Json;
using EndToEnd.Test.Fakes;
using EndToEnd.Test.Routes;
using Xunit;

namespace EndToEnd.Test.OutboundPayment.Features;

public class SubmitPaymentTests : PaymentsEndToEndTestBase
{
    public SubmitPaymentTests(
        BuildingBlocks.TestBase.TestFixture<
            Api.Program,
            Payments.FPS.Data.PaymentsDbContext,
            Payments.FPS.Data.PaymentsReadDbContext
        > fixture
    )
        : base(fixture) { }

    [Fact]
    public async Task post_returns_created()
    {
        var response = await Fixture.HttpClient.PostAsJsonAsync(
            ApiRoutes.Payments.SubmitPayment,
            FakeSubmitPaymentCommand.Create()
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
