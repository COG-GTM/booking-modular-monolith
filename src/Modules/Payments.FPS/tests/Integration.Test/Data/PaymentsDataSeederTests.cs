using BuildingBlocks.EFCore;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Payments.FPS.Data.Seed;
using Payments.FPS.OutboundPayments.Enums;
using Payments.FPS.OutboundPayments.Models;
using Xunit;

namespace Integration.Test.Data;

public class PaymentsDataSeederTests : PaymentsIntegrationTestBase
{
    public PaymentsDataSeederTests(
        BuildingBlocks.TestBase.TestFixture<
            Api.Program,
            Payments.FPS.Data.PaymentsDbContext,
            Payments.FPS.Data.PaymentsReadDbContext
        > fixture
    )
        : base(fixture) { }

    private static readonly Payments.FPS.OutboundPayments.Models.OutboundPayment SeedPayment =
        InitialData.OutboundPayments.Single();

    private async Task RunSeederAsync()
    {
        using var scope = Fixture.ServiceProvider.CreateScope();
        var seeder = scope.ServiceProvider.GetServices<IDataSeeder>().OfType<PaymentsDataSeeder>().Single();
        await seeder.SeedAllAsync();
    }

    private Task<int> SqlCountAsync() =>
        Fixture.ExecuteDbContextAsync(db => EntityFrameworkQueryableExtensions.CountAsync(db.OutboundPayments));

    private Task<int> ClearSqlAsync() => Fixture.ExecuteDbContextAsync(db => db.OutboundPayments.ExecuteDeleteAsync());

    private Task<List<OutboundPaymentReadModel>> MongoDocumentsAsync() =>
        Fixture.ExecuteReadContextAsync(db => db.OutboundPayment.Find(_ => true).ToListAsync());

    private Task DropMongoCollectionAsync() =>
        Fixture.ExecuteReadContextAsync(db =>
            db.OutboundPayment.Database.DropCollectionAsync(db.OutboundPayment.CollectionNamespace.CollectionName)
        );

    [Fact]
    public async Task should_seed_read_model_when_sql_is_already_seeded()
    {
        (await SqlCountAsync()).Should().Be(1);
        await DropMongoCollectionAsync();
        (await MongoDocumentsAsync()).Should().BeEmpty();

        await RunSeederAsync();

        (await SqlCountAsync()).Should().Be(1);
        var documents = await MongoDocumentsAsync();
        var document = documents.Should().ContainSingle().Subject;
        document.OutboundPaymentId.Should().Be(SeedPayment.Id.Value);
        document.Amount.Should().Be(SeedPayment.Amount.Value);
        document.Currency.Should().Be(SeedPayment.Amount.Currency);
        document.DebtorSortCode.Should().Be(SeedPayment.DebtorAccount.SortCode);
        document.DebtorAccountNumber.Should().Be(SeedPayment.DebtorAccount.AccountNumber);
        document.CreditorSortCode.Should().Be(SeedPayment.CreditorAccount.SortCode);
        document.CreditorAccountNumber.Should().Be(SeedPayment.CreditorAccount.AccountNumber);
        document.Reference.Should().Be(SeedPayment.Reference);
        document.Status.Should().Be(PaymentStatus.Initiated);
        document.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task should_seed_sql_when_read_model_is_already_seeded()
    {
        await ClearSqlAsync();
        (await SqlCountAsync()).Should().Be(0);
        (await MongoDocumentsAsync()).Should().ContainSingle();

        await RunSeederAsync();

        var payment = await Fixture.ExecuteDbContextAsync(db => db.OutboundPayments.FindAsync(SeedPayment.Id));
        payment.Should().NotBeNull();
        payment!.Reference.Should().Be(SeedPayment.Reference);
        payment.Status.Should().Be(PaymentStatus.Initiated);
        (await SqlCountAsync()).Should().Be(1);
        (await MongoDocumentsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task should_not_duplicate_seed_data_when_both_stores_are_seeded()
    {
        (await SqlCountAsync()).Should().Be(1);
        (await MongoDocumentsAsync()).Should().ContainSingle();

        await RunSeederAsync();
        await RunSeederAsync();

        (await SqlCountAsync()).Should().Be(1);
        (await MongoDocumentsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task should_seed_both_stores_when_both_are_empty()
    {
        await ClearSqlAsync();
        await DropMongoCollectionAsync();

        await RunSeederAsync();

        (await SqlCountAsync()).Should().Be(1);
        var documents = await MongoDocumentsAsync();
        documents.Should().ContainSingle().Which.OutboundPaymentId.Should().Be(SeedPayment.Id.Value);
    }
}
