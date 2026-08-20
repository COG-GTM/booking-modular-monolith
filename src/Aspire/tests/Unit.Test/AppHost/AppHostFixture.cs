using Aspire.Hosting.Testing;
using Xunit;

namespace Unit.Test.AppHost;

public sealed class AppHostFixture : IAsyncLifetime
{
    public IDistributedApplicationTestingBuilder Builder { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        Builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>();
    }

    public async Task DisposeAsync()
    {
        if (Builder is not null)
        {
            await Builder.DisposeAsync();
        }
    }
}
