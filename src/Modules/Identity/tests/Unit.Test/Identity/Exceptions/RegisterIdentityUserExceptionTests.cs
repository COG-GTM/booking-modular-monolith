namespace Unit.Test.Identity.Exceptions;

using System.Net;
using BuildingBlocks.Exception;
using FluentAssertions;
using global::Identity.Identity.Exceptions;
using Xunit;

public class RegisterIdentityUserExceptionTests
{
    [Fact]
    public void should_be_an_app_exception_with_bad_request_status_code()
    {
        var exception = new RegisterIdentityUserException("some error");

        exception.Should().BeAssignableTo<AppException>();
        exception.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        exception.Message.Should().Be("some error");
    }
}
