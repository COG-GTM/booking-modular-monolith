using Api;
using BuildingBlocks.TestBase;
using Payments.FPS.Data;
using Xunit;

namespace EndToEnd.Test;

[Collection("Payments.FPS EndToEnd Test")]
public class PaymentsEndToEndTestBase(TestFixture<Api.Program, PaymentsDbContext, PaymentsReadDbContext> fixture)
    : TestBase<Api.Program, PaymentsDbContext, PaymentsReadDbContext>(fixture) { }

[CollectionDefinition("Payments.FPS EndToEnd Test")]
public class PaymentsEndToEndTestCollection
    : ICollectionFixture<TestFixture<Api.Program, PaymentsDbContext, PaymentsReadDbContext>> { }
