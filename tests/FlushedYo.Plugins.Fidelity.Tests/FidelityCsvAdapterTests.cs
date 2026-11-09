using FlushedYo.Plugins.Fidelity.Tests.Support;

namespace FlushedYo.Plugins.Fidelity.Tests;

public sealed class FidelityCsvAdapterTests
{
    private static readonly FidelityCsvAdapter Adapter = new(InstitutionIdentity.Fidelity);

    [Fact]
    public void MapsSignedAmountsAndAccountScopedIdentity()
    {
        var csv = FidelityCsvFixture.Export(
            FidelityCsvFixture.Row("X100", "2026-11-01", "-12.50", new() { Description = "Coffee" }),
            FidelityCsvFixture.Row("X100", "2026-11-02", "+30.00", new() { Description = "Refund" }));

        var result = Adapter.Adapt(csv);

        Assert.Empty(result.Ambiguous);
        Assert.Equal(2, result.Transactions.Count);
        Assert.Equal(-12.50m, result.Transactions[0].Amount);
        Assert.Equal(30.00m, result.Transactions[1].Amount);
        Assert.Equal(new DateOnly(2026, 11, 1), result.Transactions[0].Date);
        Assert.Equal("fidelity", result.Transactions[0].Institution);
        Assert.All(result.Transactions, tx => Assert.Equal(64, tx.ExternalId.Length));
    }

    [Fact]
    public void ParenthesizedAmountIsNegative()
    {
        var csv = FidelityCsvFixture.Export(
            FidelityCsvFixture.Row("X100", "2026-11-01", "($9.99)", new() { Description = "Fee" }));

        var result = Adapter.Adapt(csv);

        Assert.Empty(result.Ambiguous);
        Assert.Equal(-9.99m, result.Transactions[0].Amount);
    }

    [Fact]
    public void UnsignedAmountIsRefusedRatherThanGuessed()
    {
        var csv = FidelityCsvFixture.Export(
            FidelityCsvFixture.Row("X100", "2026-11-01", "10.00"));

        var result = Adapter.Adapt(csv);

        Assert.Empty(result.Transactions);
        Assert.Equal(AmbiguousReason.AmbiguousSign, Assert.Single(result.Ambiguous).Reason);
    }

    [Fact]
    public void UnreadableDateIsRefused()
    {
        var csv = FidelityCsvFixture.Export(
            FidelityCsvFixture.Row("X100", "not-a-date", "-1.00"));

        var result = Adapter.Adapt(csv);

        Assert.Equal(AmbiguousReason.MissingDate, Assert.Single(result.Ambiguous).Reason);
    }

    [Fact]
    public void MalformedRowWithWrongFieldCountIsRefused()
    {
        var csv = FidelityCsvFixture.Export(
            "2026-11-01,Buy,TEST,Short row,X100");

        var result = Adapter.Adapt(csv);

        Assert.Equal(AmbiguousReason.MalformedRow, Assert.Single(result.Ambiguous).Reason);
    }

    [Fact]
    public void MissingAmountColumnFailsClosed()
    {
        var csv = "Run Date,Account Number,Description\n2026-11-01,X100,Coffee\n";

        Assert.Throws<FidelityAdapterException>(() => Adapter.Adapt(csv));
    }

    [Fact]
    public void HeaderOnlyExportFailsClosed()
    {
        Assert.Throws<FidelityAdapterException>(() => Adapter.Adapt(string.Empty));
        Assert.Throws<FidelityAdapterException>(() => Adapter.Adapt("   \n"));
    }

    [Fact]
    public void QuotedFieldWithCommaAndEscapedQuoteIsPreserved()
    {
        var csv = FidelityCsvFixture.Export(
            FidelityCsvFixture.Row("X100", "2026-11-01", "-1.00", new() { Description = "\"ACME, Inc \"\"Store\"\"\"" }));

        var result = Adapter.Adapt(csv);

        Assert.Empty(result.Ambiguous);
        Assert.Equal("ACME, Inc \"Store\"", result.Transactions[0].Payee);
    }

    [Fact]
    public void RowFromAnotherAccountIsRefusedAsMismatch()
    {
        var csv = FidelityCsvFixture.Export(
            FidelityCsvFixture.Row("X100", "2026-11-01", "-1.00"),
            FidelityCsvFixture.Row("X999", "2026-11-02", "-2.00"));

        var result = Adapter.Adapt(csv, FidelityCsvFixture.AccountKey("X100"));

        var transaction = Assert.Single(result.Transactions);
        Assert.Equal(-1.00m, transaction.Amount);
        Assert.Equal(AmbiguousReason.AccountMismatch, Assert.Single(result.Ambiguous).Reason);
    }

    [Fact]
    public void SameDayIdenticalRowsShareAnIdentity()
    {
        var csv = FidelityCsvFixture.Export(
            FidelityCsvFixture.Row("X100", "2026-11-01", "-5.00", new() { Description = "Same" }),
            FidelityCsvFixture.Row("X100", "2026-11-01", "-5.00", new() { Description = "Same" }));

        var result = Adapter.Adapt(csv);

        Assert.Equal(2, result.Transactions.Count);
        Assert.Equal(result.Transactions[0].ExternalId, result.Transactions[1].ExternalId);
    }

    [Fact]
    public void DateTimeStampRowsAreAcceptedByRunDate()
    {
        var csv = FidelityCsvFixture.Export(
            FidelityCsvFixture.Row("X100", "11/01/2026", "-3.00"));

        var result = Adapter.Adapt(csv);

        Assert.Empty(result.Ambiguous);
        Assert.Equal(new DateOnly(2026, 11, 1), result.Transactions[0].Date);
    }
}
