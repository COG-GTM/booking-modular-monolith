using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using Xunit;

namespace BuildingBlocks.Tests;

public class PersistMessageWorkerStoreIdentityTests
{
    [Fact]
    public void store_identity_normalizes_host_case_and_default_port_but_not_database_or_port()
    {
        var stores = new PersistMessageWorkerStores();
        stores.TryClaim("Host=LOCALHOST;Database=shared;Username=first").Should().BeTrue();
        stores.TryClaim("Server=localhost;Port=5432;Database=shared;Username=second").Should().BeFalse();
        stores.TryClaim("Host=localhost;Port=5433;Database=shared;Username=first").Should().BeTrue();
        stores.TryClaim("Host=localhost;Database=other;Username=first").Should().BeTrue();
        stores.Release("Host=localhost;Port=5432;Database=shared;Username=second");
        stores.TryClaim("Host=localhost;Database=shared;Username=third").Should().BeTrue();
        stores.TryClaim("Host=localhost;Port=5433;Database=shared;Username=second").Should().BeFalse();
    }
}
