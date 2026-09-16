using Api;
using BuildingBlocks.TestBase;
using Payments.FPS.Data;
using Xunit;

namespace Integration.Test;

[Collection("Payments.FPS Integration Test")]
public class PaymentsIntegrationTestBase(TestFixture<Api.Program, PaymentsDbContext, PaymentsReadDbContext> fixture)
    : TestBase<Api.Program, PaymentsDbContext, PaymentsReadDbContext>(fixture) { }

[CollectionDefinition("Payments.FPS Integration Test")]
public class PaymentsIntegrationTestCollection
    : ICollectionFixture<TestFixture<Api.Program, PaymentsDbContext, PaymentsReadDbContext>> { }
