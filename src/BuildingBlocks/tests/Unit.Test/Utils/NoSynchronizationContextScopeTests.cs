using BuildingBlocks.Utils;
using FluentAssertions;
using Xunit;

namespace BuildingBlocks.Unit.Test.UtilsTests;

public class NoSynchronizationContextScopeTests
{
    private sealed class UtilsTestSyncContext : SynchronizationContext;

    [Fact]
    public void enter_clears_the_current_context_and_dispose_restores_it()
    {
        var original = SynchronizationContext.Current;
        var custom = new UtilsTestSyncContext();
        SynchronizationContext.SetSynchronizationContext(custom);
        try
        {
            using (NoSynchronizationContextScope.Enter())
            {
                SynchronizationContext.Current.Should().BeNull();
            }

            SynchronizationContext.Current.Should().BeSameAs(custom);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(original);
        }
    }
}
