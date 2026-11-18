namespace Unit.Test.Flight.Features.Handlers.GetAvailableFlights;

using FluentAssertions;
using FluentValidation.TestHelper;
using global::Flight.Flights.Features.GettingAvailableFlights.V1;
using global::Flight.Flights.Models;
using MongoDB.Driver;
using Xunit;
using GetAvailableFlightsQuery = global::Flight.Flights.Features.GettingAvailableFlights.V1.GetAvailableFlights;

public class GetAvailableFlightsUnboundedQueryTests
{
    private static IMongoCollection<FlightReadModel> Collection() =>
        new MongoClient("mongodb://localhost:27017")
            .GetDatabase("unit-test")
            .GetCollection<FlightReadModel>("flight");

    [Fact]
    public void query_should_filter_deleted_flights_and_limit_to_one_page_in_mongodb()
    {
        var request = new GetAvailableFlightsQuery(PageNumber: 3, PageSize: 25);

        var mql = GetAvailableFlightsHandler.BuildQuery(Collection().AsQueryable(), request).ToString();

        mql.Should().Contain("{ \"$match\" : { \"IsDeleted\" : false } }");
        mql.Should().Contain("{ \"$skip\" : NumberLong(50) }").And.Subject.Should().Contain("{ \"$limit\" : NumberLong(25) }");
    }

    [Fact]
    public void default_query_should_use_default_page_size()
    {
        var request = new GetAvailableFlightsQuery();

        var mql = GetAvailableFlightsHandler.BuildQuery(Collection().AsQueryable(), request).ToString();

        request.PageNumber.Should().Be(1);
        request.PageSize.Should().Be(GetAvailableFlightsQuery.DefaultPageSize);
        mql.Should().Contain($"{{ \"$limit\" : NumberLong({GetAvailableFlightsQuery.DefaultPageSize}) }}");
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    [InlineData(1, GetAvailableFlightsQuery.MaxPageSize + 1)]
    [InlineData(1, int.MaxValue)]
    [InlineData(0, 10)]
    [InlineData(GetAvailableFlightsQuery.MaxPageNumber + 1, 10)]
    public void validator_should_reject_out_of_range_paging(int pageNumber, int pageSize)
    {
        var result = new GetAvailableFlightsValidator().TestValidate(new GetAvailableFlightsQuery(pageNumber, pageSize));

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, GetAvailableFlightsQuery.MaxPageSize)]
    [InlineData(GetAvailableFlightsQuery.MaxPageNumber, GetAvailableFlightsQuery.MaxPageSize)]
    public void validator_should_accept_in_range_paging(int pageNumber, int pageSize)
    {
        var result = new GetAvailableFlightsValidator().TestValidate(new GetAvailableFlightsQuery(pageNumber, pageSize));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void cache_key_should_be_per_page_and_share_the_invalidation_prefix()
    {
        var key = new GetAvailableFlightsQuery(2, 50).CacheKey;

        key.Should().Be("GetAvailableFlights:2:50");
        key.Should().StartWith(new global::Flight.Flights.Features.UpdatingFlight.V1.UpdateFlight(
            default, "x", default, default, default, default, default, default, default, default, default, default).CacheKey);
    }
}
