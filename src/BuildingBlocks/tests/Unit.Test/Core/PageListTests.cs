using BuildingBlocks.Core.Pagination;
using FluentAssertions;
using Xunit;

namespace BuildingBlocks.Unit.Test.CoreTests;

public class PageListTests
{
    private sealed class CorePageItem;

    [Theory]
    [InlineData(1, 10, 25, 10, 1, 10, 3, false, true)]
    [InlineData(2, 10, 25, 10, 11, 20, 3, true, true)]
    [InlineData(3, 10, 25, 5, 21, 25, 3, true, false)]
    [InlineData(1, 10, 0, 0, 0, 0, 0, false, false)]
    [InlineData(1, 10, 10, 10, 1, 10, 1, false, false)]
    public void page_list_derives_indices_total_pages_and_navigation_flags(
        int pageNumber,
        int pageSize,
        int totalCount,
        int itemsOnPage,
        int expectedStart,
        int expectedEnd,
        int expectedTotalPages,
        bool expectedHasPrevious,
        bool expectedHasNext
    )
    {
        var items = Enumerable.Range(0, itemsOnPage).Select(_ => new CorePageItem()).ToList();
        var page = PageList<CorePageItem>.Create(items, pageNumber, pageSize, totalCount);

        page.CurrentPageSize.Should().Be(itemsOnPage);
        page.CurrentStartIndex.Should().Be(expectedStart);
        page.CurrentEndIndex.Should().Be(expectedEnd);
        page.TotalPages.Should().Be(expectedTotalPages);
        page.HasPrevious.Should().Be(expectedHasPrevious);
        page.HasNext.Should().Be(expectedHasNext);
        page.TotalCount.Should().Be(totalCount);
    }

    [Fact]
    public void empty_page_list_has_zero_total_no_items_and_no_next_page()
    {
        var page = PageList<CorePageItem>.Empty;

        page.TotalCount.Should().Be(0);
        page.Items.Should().BeEmpty();
        page.HasNext.Should().BeFalse();
        page.HasPrevious.Should().BeFalse();
        page.TotalPages.Should().Be(0);
    }
}
