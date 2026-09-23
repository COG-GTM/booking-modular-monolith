using System.Transactions;
using BuildingBlocks.Core.Event;
using FluentAssertions;
using Identity.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

namespace Unit.Test.Modules;

/// <summary>
/// <see cref="IdentityContext"/> maps no <c>IAggregate</c> entity types, so <c>GetDomainEvents()</c> is always
/// empty and the behavior can only take the short-circuit path; these tests pin that down.
/// </summary>
public sealed class EfTxIdentityBehaviorTests : IDisposable
{
    private readonly TransactionBehaviorFixture _fixture = new();
    private readonly string _databaseName = DbContextFactory.NewDatabaseName();
    private readonly IdentityContext _dbContext;
    private readonly FakeCommand _request = new("identity");

    public EfTxIdentityBehaviorTests()
    {
        _dbContext = ModuleFakes.CreateIdentityContext(_databaseName);
    }

    public void Dispose() => _dbContext.Dispose();

    private EfTxIdentityBehavior<FakeCommand, string> CreateSut()
    {
        return new EfTxIdentityBehavior<FakeCommand, string>(
            Substitute.For<ILogger<EfTxIdentityBehavior<FakeCommand, string>>>(),
            _dbContext,
            _fixture.PersistMessageDbContext,
            _fixture.EventDispatcher
        );
    }

    [Fact]
    public async Task handle_without_domain_events_should_return_response_without_dispatching_or_saving()
    {
        var response = await CreateSut()
            .Handle(_request, TransactionBehaviorFixture.Handler("handled"), CancellationToken.None);

        response.Should().Be("handled");
        await _fixture
            .EventDispatcher.DidNotReceiveWithAnyArgs()
            .SendAsync(Arg.Any<IReadOnlyList<IDomainEvent>>(), Arg.Any<Type>(), Arg.Any<CancellationToken>());
        await _fixture.PersistMessageDbContext.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        Transaction.Current.Should().BeNull();
    }

    [Fact]
    public async Task handle_with_pending_user_but_no_domain_events_should_not_save_identity_context()
    {
        var user = ModuleFakes.CreateUser();

        await CreateSut()
            .Handle(
                _request,
                TransactionBehaviorFixture.Handler("handled", () => _dbContext.Users.Add(user)),
                CancellationToken.None
            );

        _dbContext.GetDomainEvents().Should().BeEmpty();
        _dbContext.Entry(user).State.Should().Be(EntityState.Added);
        await _fixture.PersistMessageDbContext.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());

        await using var verification = ModuleFakes.CreateIdentityContext(_databaseName);
        (await verification.Users.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task handle_should_rethrow_handler_failure_without_dispatching()
    {
        var act = () =>
            CreateSut()
                .Handle(
                    _request,
                    TransactionBehaviorFixture.FailingHandler<string>(new InvalidOperationException("handler failed")),
                    CancellationToken.None
                );

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("handler failed");
        await _fixture
            .EventDispatcher.DidNotReceiveWithAnyArgs()
            .SendAsync(Arg.Any<IReadOnlyList<IDomainEvent>>(), Arg.Any<Type>(), Arg.Any<CancellationToken>());
        await _fixture.PersistMessageDbContext.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
