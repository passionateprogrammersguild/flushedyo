using FlushedYo.Core.Models;
using FlushedYo.Core.Tests.Store;
using FlushedYo.Core.Tracking;

namespace FlushedYo.Core.Tests.Tracking;

/// <summary>Cash-only calculation, unknown states, and arithmetic boundaries.</summary>
public sealed class CashStateCalculatorTests
{
    [Fact]
    public void KnownBaseline_AppliesOnlyNewCashAffectingRowsAndReturnsSignedDifference()
    {
        AccountTransaction[] inserted =
        [
            Tx(-12.30m),
            Tx(500m, CashImpact.NotCashAffecting),
            Tx(2.30m),
        ];

        var state = CashStateCalculator.Calculate(100m, inserted);

        Assert.Equal(new CashState(CashStateStatus.Known, CashStateReason.None, 100m, 90m, -10m), state);
    }

    [Fact]
    public void MissingBaseline_ReturnsUnknownWithoutFabricatedValues()
    {
        var state = CashStateCalculator.Calculate(null, [Tx(25m)]);

        Assert.Equal(
            new CashState(CashStateStatus.Unknown, CashStateReason.MissingBaseline, null, null, null),
            state);
    }

    [Theory]
    [InlineData(CashImpact.Unknown, CashStateReason.AmbiguousTransaction)]
    [InlineData(CashImpact.Correction, CashStateReason.Correction)]
    [InlineData(CashImpact.Unsupported, CashStateReason.UnsupportedActivity)]
    public void UnresolvedActivity_ReturnsReconciliationNeededWithoutValues(
        CashImpact impact,
        CashStateReason expectedReason)
    {
        var state = CashStateCalculator.Calculate(100m, [Tx(25m, impact)]);

        Assert.Equal(
            new CashState(CashStateStatus.ReconciliationNeeded, expectedReason, null, null, null),
            state);
    }

    [Fact]
    public void RejectedActivity_ReturnsReconciliationNeededEvenWithBaseline()
    {
        var state = CashStateCalculator.Calculate(100m, [], hasRejectedTransactions: true);

        Assert.Equal(
            new CashState(
                CashStateStatus.ReconciliationNeeded,
                CashStateReason.AmbiguousTransaction,
                null,
                null,
                null),
            state);
    }

    [Fact]
    public void MovementOverflow_ReturnsReconciliationNeededInsteadOfThrowing()
    {
        var state = CashStateCalculator.Calculate(0m, [Tx(decimal.MaxValue), Tx(1m)]);

        Assert.Equal(
            new CashState(
                CashStateStatus.ReconciliationNeeded,
                CashStateReason.ArithmeticOverflow,
                null,
                null,
                null),
            state);
    }

    [Fact]
    public void BalanceOverflow_ReturnsReconciliationNeededInsteadOfThrowing()
    {
        var state = CashStateCalculator.Calculate(decimal.MaxValue, [Tx(1m)]);

        Assert.Equal(
            new CashState(
                CashStateStatus.ReconciliationNeeded,
                CashStateReason.ArithmeticOverflow,
                null,
                null,
                null),
            state);
    }

    [Fact]
    public void UndefinedCashImpact_FailsClosed()
    {
        var state = CashStateCalculator.Calculate(100m, [Tx(1m, (CashImpact)999)]);

        Assert.Equal(CashStateStatus.ReconciliationNeeded, state.Status);
        Assert.Equal(CashStateReason.AmbiguousTransaction, state.Reason);
        Assert.Null(state.CashBalance);
        Assert.Null(state.Difference);
    }

    [Fact]
    public void NullTransactionSequence_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => CashStateCalculator.Calculate(0m, null!));
    }

    private static AccountTransaction Tx(decimal amount, CashImpact impact = CashImpact.CashAffecting) =>
        AccountLedgerTestData.AccountTx(amount: amount, cashImpact: impact);
}
