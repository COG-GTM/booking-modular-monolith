using BuildingBlocks.Core.Event;
using FluentAssertions;
using Xunit;

namespace Unit.Test.Core;

public class DefaultEventHeadersProviderTests
{
    [Fact]
    public void get_headers_should_return_empty_dictionary()
    {
        var provider = new DefaultEventHeadersProvider();

        provider.GetHeaders().Should().BeEmpty();
    }
}
