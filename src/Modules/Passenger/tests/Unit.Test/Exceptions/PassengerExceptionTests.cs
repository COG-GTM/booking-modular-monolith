using System.Net;
using BuildingBlocks.Exception;
using FluentAssertions;
using Xunit;

namespace Unit.Test.Exceptions;

public class PassengerExceptionTests
{
    public static TheoryData<Func<Exception>, string, HttpStatusCode> ExceptionCases =>
        new()
        {
            {
                () => new global::Passenger.Exceptions.InvalidAgeException(),
                "Age Cannot be null or negative",
                HttpStatusCode.BadRequest
            },
            {
                () => new global::Passenger.Exceptions.InvalidNameException(),
                "Name cannot be empty or whitespace.",
                HttpStatusCode.BadRequest
            },
            {
                () => new global::Passenger.Exceptions.InvalidPassportNumberException(),
                "Passport number cannot be empty or whitespace.",
                HttpStatusCode.BadRequest
            },
            {
                () => new global::Passenger.Exceptions.InvalidPassengerIdException(Guid.Empty),
                $"PassengerId: '{Guid.Empty}' is invalid.",
                HttpStatusCode.BadRequest
            },
            {
                () => new global::Passenger.Exceptions.PassengerNotExist(),
                "Please register before!",
                HttpStatusCode.NotFound
            },
            {
                () => new global::Passenger.Exceptions.PassengerNotFoundException(),
                "Passenger not found!",
                HttpStatusCode.NotFound
            },
            {
                () => new global::Passenger.Passengers.Exceptions.InvalidAgeException(),
                "Age Cannot be null or negative",
                HttpStatusCode.BadRequest
            },
            {
                () => new global::Passenger.Passengers.Exceptions.InvalidNameException(),
                "Name cannot be empty or whitespace.",
                HttpStatusCode.BadRequest
            },
            {
                () => new global::Passenger.Passengers.Exceptions.InvalidPassportNumberException(),
                "Passport number cannot be empty or whitespace.",
                HttpStatusCode.BadRequest
            },
            {
                () => new global::Passenger.Passengers.Exceptions.PassengerNotExist(),
                "Please register before!",
                HttpStatusCode.BadRequest
            },
            {
                () => new global::Passenger.Passengers.Exceptions.PassengerNotFoundException(),
                "Passenger not found!",
                HttpStatusCode.NotFound
            },
        };

    [Theory]
    [MemberData(nameof(ExceptionCases))]
    public void Passenger_exceptions_expose_the_expected_message_and_status(
        Func<Exception> createException,
        string message,
        HttpStatusCode statusCode
    )
    {
        var exception = createException();

        exception.Message.Should().Be(message);
        exception.Should().BeAssignableTo<CustomException>().Which.StatusCode.Should().Be(statusCode);
    }
}
