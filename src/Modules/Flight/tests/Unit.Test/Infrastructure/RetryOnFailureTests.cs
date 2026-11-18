using BuildingBlocks.Polly;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Unit.Test.Infrastructure;

public class RetryOnFailureTests
{
    public RetryOnFailureTests()
    {
        Extensions.Logger = NullLogger.Instance;
    }

    [Fact]
    public void retry_on_failure_should_return_result_without_retrying_when_action_succeeds()
    {
        // Arrange
        var attempts = 0;

        // Act
        var result = this.RetryOnFailure(() =>
        {
            attempts++;
            return 42;
        });

        // Assert
        result.Should().Be(42);
        attempts.Should().Be(1);
    }

    [Fact]
    public void retry_on_failure_should_retry_transient_exceptions_up_to_retry_count()
    {
        // Arrange
        var attempts = 0;

        // Act
        var act = () =>
            this.RetryOnFailure<int>(
                () =>
                {
                    attempts++;
                    throw new InvalidOperationException("transient");
                },
                retryCount: 3
            );

        // Assert
        act.Should().Throw<InvalidOperationException>();
        attempts.Should().Be(4);
    }

    [Fact]
    public void retry_on_failure_should_not_retry_db_update_concurrency_exception()
    {
        // Arrange
        var attempts = 0;

        // Act
        var act = () =>
            this.RetryOnFailure<int>(
                () =>
                {
                    attempts++;
                    throw new DbUpdateConcurrencyException("conflict");
                },
                retryCount: 3
            );

        // Assert: a concurrency conflict can never succeed on retry, so the conflicting write must not be repeated
        act.Should().Throw<DbUpdateConcurrencyException>();
        attempts.Should().Be(1);
    }
}
