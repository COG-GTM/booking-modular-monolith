using BuildingBlocks.Core.Pagination;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Sieve.Attributes;
using Sieve.Services;
using Xunit;

namespace BuildingBlocks.Unit.Test.CoreTests;

public class ApplyPagingTests
{
    private sealed class CoreSieveItem
    {
        [Sieve(CanFilter = true, CanSort = true)]
        public int Score { get; init; }

        public string Name { get; init; } = string.Empty;
    }

    private sealed record CorePageRequest : IPageRequest
    {
        public int PageNumber { get; init; }
        public int PageSize { get; init; }
        public string? Filters { get; init; }
        public string? SortOrder { get; init; }
    }

    private static IQueryable<CoreSieveItem> Items() =>
        Enumerable.Range(1, 25).Select(i => new CoreSieveItem { Score = i, Name = $"item-{i}" }).AsQueryable();

    private static SieveProcessor Sieve() => new(Options.Create(new Sieve.Models.SieveOptions()));

    [Fact]
    public async Task second_page_of_ten_returns_ten_items_and_the_unpaginated_total()
    {
        var request = new CorePageRequest { PageNumber = 2, PageSize = 10 };

        var page = await Items().ApplyPagingAsync(request, Sieve());

        page.Items.Should().HaveCount(10);
        page.TotalCount.Should().Be(25);
        page.Items.Select(i => i.Score).Should().Equal(Enumerable.Range(11, 10));
        page.Items.Should().BeAssignableTo<IReadOnlyList<CoreSieveItem>>();
    }

    [Fact]
    public async Task sieve_filter_reduces_the_total_count_to_the_filtered_rows()
    {
        var request = new CorePageRequest
        {
            PageNumber = 1,
            PageSize = 10,
            Filters = "Score>=20",
        };

        var page = await Items().ApplyPagingAsync(request, Sieve());

        page.TotalCount.Should().Be(6);
    }
}
