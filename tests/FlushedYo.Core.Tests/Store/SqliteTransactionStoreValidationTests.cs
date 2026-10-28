using FlushedYo.Core.Store;
using static FlushedYo.Core.Tests.Store.TransactionTestData;

namespace FlushedYo.Core.Tests.Store;

/// <summary>Argument validation, SQL safety, and cancellation (spec: sqlite_transaction_store).</summary>
public sealed class SqliteTransactionStoreValidationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task HasTransactionAsync_BlankHash_Throws(string? hash)
    {
        using var ledger = new TempLedger();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => CreateStore(ledger).HasTransactionAsync(hash!, Token));
    }

    [Fact]
    public async Task RecordTransactionAsync_NullTransaction_Throws()
    {
        using var ledger = new TempLedger();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            CreateStore(ledger).RecordTransactionAsync(null!, Audit(Tx()), Token));
    }

    [Fact]
    public async Task RecordTransactionAsync_NullAudit_Throws()
    {
        using var ledger = new TempLedger();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            CreateStore(ledger).RecordTransactionAsync(Tx(), null!, Token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task RecordTransactionAsync_BlankPluginVersion_Throws(string version)
    {
        using var ledger = new TempLedger();
        var transaction = Tx();

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            CreateStore(ledger).RecordTransactionAsync(transaction, Audit(transaction, version), Token));
        Assert.Equal(0, await CountRowsAsync(ledger, Token));
    }

    [Fact]
    public async Task RecordTransactionAsync_MismatchedHash_ThrowsWithoutLeakingValues()
    {
        using var ledger = new TempLedger();
        var transaction = Tx(payee: "SECRET-PAYEE");
        var audit = new TransactionAudit(new string('0', 64), "1.0.0", DefaultTime);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateStore(ledger).RecordTransactionAsync(transaction, audit, Token));

        Assert.DoesNotContain("SECRET-PAYEE", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, await CountRowsAsync(ledger, Token));
    }

    [Fact]
    public async Task RecordTransactionAsync_InvalidTransaction_ThrowsWithoutLeakingValues()
    {
        using var ledger = new TempLedger();
        var transaction = Tx(externalId: "", payee: "SECRET-PAYEE");
        var audit = new TransactionAudit(new string('0', 64), "1.0.0", DefaultTime);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateStore(ledger).RecordTransactionAsync(transaction, audit, Token));

        Assert.DoesNotContain("SECRET-PAYEE", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, await CountRowsAsync(ledger, Token));
    }

    [Fact]
    public async Task SqlInjectionAttempt_IsStoredLiterally()
    {
        using var ledger = new TempLedger();
        var store = CreateStore(ledger);
        var transaction = Tx(payee: "x'); DROP TABLE transactions;--");

        Assert.True(await store.RecordTransactionAsync(transaction, Audit(transaction), Token));

        Assert.Equal([transaction], await store.GetTransactionsAsync(Token));
        Assert.Equal(1, await CountRowsAsync(ledger, Token));
    }

    [Fact]
    public async Task PreCancelledToken_ThrowsForEveryOperation()
    {
        using var ledger = new TempLedger();
        var store = CreateStore(ledger);
        var transaction = Tx();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.HasTransactionAsync("hash", cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.RecordTransactionAsync(transaction, Audit(transaction), cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.GetTransactionsAsync(cts.Token));
    }
}
