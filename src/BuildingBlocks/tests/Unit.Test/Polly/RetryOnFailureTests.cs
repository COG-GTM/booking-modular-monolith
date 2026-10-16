using BuildingBlocks.Polly;
using BuildingBlocks.Unit.Test.Support;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace BuildingBlocks.Unit.Test.PollyTests;

[CollectionDefinition("Polly static state", DisableParallelization = true)]
public class PollyStaticStateCollection;

[Collection("Polly static state")]
public class RetryOnFailureTests
{
    [Fact]
    public void action_failing_twice_then_succeeding_returns_the_value_after_retries()
    {
        var logger = new RecordingLogger();
        var previous = Extensions.Logger;
        Extensions.Logger = logger;
        try
        {
            var invocations = 0;

            var result = new object().RetryOnFailure(() =>
            {
                invocations++;
                if (invocations < 3)
                    throw new InvalidOperationException($"failure {invocations}");
                return "ok";
            });

            result.Should().Be("ok");
            invocations.Should().Be(3);
            logger.Entries.Should().Contain(e => e.Message == "Retry attempt: 1");
            logger.Entries.Should().Contain(e => e.Message == "Retry attempt: 2");
        }
        finally
        {
            Extensions.Logger = previous;
        }
    }

    [Fact]
    public void always_failing_action_rethrows_the_original_exception_after_all_retries()
    {
        var logger = new RecordingLogger();
        var previous = Extensions.Logger;
        Extensions.Logger = logger;
        try
        {
            var invocations = 0;

            var act = () =>
                new object().RetryOnFailure<string>(
                    () =>
                    {
                        invocations++;
                        throw new InvalidOperationException("always fails");
                    },
                    retryCount: 2
                );

            var ex = act.Should().Throw<InvalidOperationException>();
            ex.Which.Message.Should().Be("always fails");
            invocations.Should().Be(3);
        }
        finally
        {
            Extensions.Logger = previous;
        }
    }
}
