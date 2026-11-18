using BuildingBlocks.Core;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using Integration.Test.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Integration.Test.Identity.Infrastructure;

using global::Identity.Data;
using global::Identity.Identity.Features.RegisteringNewUser.V1;

public class EfTxIdentityBehaviorLoggingTests
{
    [Fact]
    public async Task handle_should_not_log_register_new_user_password_passport_or_email()
    {
        var command = new RegisterNewUser(
            "Jane",
            "Doe",
            "jane.doe",
            "jane.doe@example.com",
            "S3cr3t-P@ssw0rd!",
            "S3cr3t-P@ssw0rd!",
            "PX-998877"
        );
        var expected = new RegisterNewUserResult(
            Guid.NewGuid(),
            command.FirstName,
            command.LastName,
            command.Username,
            command.PassportNumber
        );

        var logger = new CapturingLogger<EfTxIdentityBehavior<RegisterNewUser, RegisterNewUserResult>>();
        await using var identityContext = new IdentityContext(
            new DbContextOptionsBuilder<IdentityContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options
        );

        var behavior = new EfTxIdentityBehavior<RegisterNewUser, RegisterNewUserResult>(
            logger,
            identityContext,
            Substitute.For<IPersistMessageDbContext>(),
            Substitute.For<IEventDispatcher>()
        );

        var response = await behavior.Handle(command, _ => Task.FromResult(expected), CancellationToken.None);

        response.Should().BeSameAs(expected);
        logger.Entries.Should().NotBeEmpty();
        logger
            .Entries.Should()
            .NotContain(e =>
                e.Message.Contains(command.Password)
                || e.Message.Contains(command.PassportNumber)
                || e.Message.Contains(command.Email)
            );
        logger.Entries.SelectMany(e => e.State).Should().NotContain(p => p.Key == "RequestContent");
        logger
            .Entries.Should()
            .Contain(e =>
                e.Level == LogLevel.Information
                && e.State.Any(p => p.Key == "MediatrRequest" && Equals(p.Value, typeof(RegisterNewUser).FullName))
            );
    }
}
