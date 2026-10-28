using FlushedYo.Core.Models;
using static FlushedYo.Core.Tests.Store.TransactionTestData;

namespace FlushedYo.Core.Tests.Store;

/// <summary>Insert, query, and round-trip behavior (spec: sqlite_transaction_store).</summary>
public sealed class SqliteTransactionStoreTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task HasTransactionAsync_OnEmptyLedger_ReturnsFalse()
    {
        using var ledger = new TempLedger();

        Assert.False(await CreateStore(ledger).HasTransactionAsync("not-there", Token));
    }

    [Fact]
    public async Task GetTransactionsAsync_OnEmptyLedger_ReturnsEmpty()
    {
        using var ledger = new TempLedger();

        Assert.Empty(await CreateStore(ledger).GetTransactionsAsync(Token));
    }

    [Fact]
    public async Task RecordTransactionAsync_NewTransaction_ReturnsTrueAndIsFound()
    {
        using var ledger = new TempLedger();
        var store = CreateStore(ledger);
        var audit = Audit(Tx());

        Assert.True(await store.RecordTransactionAsync(Tx(), audit, Token));
        Assert.True(await store.HasTransactionAsync(audit.CanonicalHash, Token));
    }

    [Fact]
    public async Task GetTransactionsAsync_ReturnsInsertionOrderWithOriginalValues()
    {
        using var ledger = new TempLedger();
        var store = CreateStore(ledger);
        Transaction[] originals =
        [
            Tx(institution: "Fidelity", externalId: " ab ", amount: -12.30m, payee: "Café | Bar", type: ""),
            Tx(externalId: "2", amount: 0.005m, date: new DateOnly(2026, 12, 31), payee: "Sub-cent"),
            Tx(externalId: "3", amount: 1234567.89m, date: new DateOnly(2025, 2, 28), payee: "東京 Store", type: "credit"),
        ];

        foreach (var original in originals)
        {
            Assert.True(await store.RecordTransactionAsync(original, Audit(original), Token));
        }

        Assert.Equal(originals, await store.GetTransactionsAsync(Token));
    }

    [Fact]
    public async Task RecordTransactionAsync_StoresOriginalValuesInInvariantFormats()
    {
        using var ledger = new TempLedger();
        var transaction = Tx(institution: "Fidelity", amount: -12.30m, payee: "  Corner Store ");

        await CreateStore(ledger).RecordTransactionAsync(transaction, Audit(transaction), Token);

        var row = Assert.Single(await ReadRowsAsync(ledger, Token));
        Assert.Equal("-12.30", row.Amount);
        Assert.Equal("2026-01-02", row.Date);
        Assert.Equal("  Corner Store ", row.Payee);
    }

    [Fact]
    public async Task RecordTransactionAsync_StoresAuditValues()
    {
        using var ledger = new TempLedger();
        var transaction = Tx();
        var audit = Audit(transaction, "2.5.1", new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero));

        await CreateStore(ledger).RecordTransactionAsync(transaction, audit, Token);

        var row = Assert.Single(await ReadRowsAsync(ledger, Token));
        Assert.Equal(audit.CanonicalHash, row.CanonicalHash);
        Assert.Equal("2.5.1", row.PluginVersion);
        Assert.Equal("2026-03-04T05:06:07.0000000Z", row.DownloadedAt);
    }

    [Fact]
    public async Task RecordTransactionAsync_NonUtcDownloadTime_IsStoredAsTheUtcInstant()
    {
        using var ledger = new TempLedger();
        var transaction = Tx();
        var audit = Audit(transaction, at: new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(5)));

        await CreateStore(ledger).RecordTransactionAsync(transaction, audit, Token);

        Assert.Equal("2026-01-01T22:04:05.0000000Z", Assert.Single(await ReadRowsAsync(ledger, Token)).DownloadedAt);
    }

    [Fact]
    public async Task Data_PersistsAcrossStoreInstances()
    {
        using var ledger = new TempLedger();
        var transaction = Tx();
        await CreateStore(ledger).RecordTransactionAsync(transaction, Audit(transaction), Token);

        var second = CreateStore(ledger);

        Assert.Equal([transaction], await second.GetTransactionsAsync(Token));
        Assert.True(await second.HasTransactionAsync(Audit(transaction).CanonicalHash, Token));
    }
}
