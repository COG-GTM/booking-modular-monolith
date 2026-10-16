using System.Net;
using BuildingBlocks.Exception;
using FluentAssertions;
using Xunit;
using DomainException = SmartCharging.Infrastructure.Exceptions.DomainException;

namespace BuildingBlocks.Unit.Test.ExceptionTests;

public class ExceptionStatusCodesTests
{
    [Fact]
    public void bad_request_exception_maps_to_400()
    {
        var ex = new BadRequestException("bad input");

        ex.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ex.Message.Should().Be("bad input");
        ex.Code.Should().BeNull();
    }

    [Fact]
    public void conflict_exception_maps_to_409()
    {
        var ex = new ConflictException("already exists", code: 7);

        ex.StatusCode.Should().Be(HttpStatusCode.Conflict);
        ex.Message.Should().Be("already exists");
        ex.Code.Should().Be(7);
    }

    [Fact]
    public void not_found_exception_maps_to_404()
    {
        var ex = new NotFoundException("missing");

        ex.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ex.Message.Should().Be("missing");
    }

    [Fact]
    public void validation_exception_maps_to_400()
    {
        var ex = new BuildingBlocks.Exception.ValidationException("name is required");

        ex.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ex.Message.Should().Be("name is required");
    }

    [Fact]
    public void app_exception_defaults_to_400_and_keeps_a_custom_status()
    {
        new AppException("boom").StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var custom = new AppException("gone", HttpStatusCode.NotFound);
        custom.StatusCode.Should().Be(HttpStatusCode.NotFound);
        custom.Message.Should().Be("gone");
    }

    [Fact]
    public void app_exception_keeps_the_inner_exception()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new AppException("outer", inner);

        ex.InnerException.Should().BeSameAs(inner);
        ex.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public void custom_exception_defaults_to_500_for_all_three_ctors()
    {
        new CustomException("msg").StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        new CustomException("msg", new InvalidOperationException())
            .StatusCode.Should()
            .Be(HttpStatusCode.InternalServerError);
        new CustomException().StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public void domain_exception_defaults_to_400()
    {
        var ex = new DomainException("domain rule violated");

        ex.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ex.Message.Should().Be("domain rule violated");
    }

    [Fact]
    public void aggregate_not_found_for_t_builds_a_typed_message()
    {
        var id = Guid.NewGuid();

        var ex = AggregateNotFoundException.For<ExceptionStatusCodesTests>(id);

        ex.Message.Should().Be($"{nameof(ExceptionStatusCodesTests)} with id '{id}' was not found");
    }
}
