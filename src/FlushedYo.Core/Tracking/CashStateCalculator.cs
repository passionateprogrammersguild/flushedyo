using FlushedYo.Core.Models;

namespace FlushedYo.Core.Tracking;

/// <summary>Pure cash-only state calculation over an opening baseline and newly inserted rows.</summary>
public static class CashStateCalculator
{
    /// <summary>
    /// Calculates a new cash balance and signed difference. Only newly inserted transactions explicitly
    /// classified as <see cref="CashImpact.CashAffecting"/> contribute; duplicates must not be supplied.
    /// </summary>
    /// <param name="openingCashBalance">
    /// Opening cash baseline, or <see langword="null"/> when neither a supplied baseline nor a previously
    /// known cash balance exists.
    /// </param>
    /// <param name="newlyInsertedTransactions">New rows in insertion order, never the whole downloaded batch.</param>
    /// <param name="hasRejectedTransactions">Whether the import also contained invalid or ambiguous transactions.</param>
    /// <returns>
    /// A known cash state only when all inputs are explicit. Missing baselines produce an unknown state;
    /// rejected, ambiguous, correction, and unsupported activity produce reconciliation-needed states.
    /// Decimal overflow also produces a reconciliation-needed state. Unknown and reconciliation-needed
    /// states never contain fabricated balances or differences.
    /// </returns>
    public static CashState Calculate(
        decimal? openingCashBalance,
        IEnumerable<AccountTransaction> newlyInsertedTransactions,
        bool hasRejectedTransactions = false)
    {
        ArgumentNullException.ThrowIfNull(newlyInsertedTransactions);

        if (hasRejectedTransactions)
        {
            return ReconciliationNeeded(CashStateReason.AmbiguousTransaction);
        }

        decimal movement = 0m;
        try
        {
            foreach (var accountTransaction in newlyInsertedTransactions)
            {
                ArgumentNullException.ThrowIfNull(accountTransaction);

                switch (accountTransaction.CashImpact)
                {
                    case CashImpact.CashAffecting:
                        movement = checked(movement + accountTransaction.Transaction.Amount);
                        break;
                    case CashImpact.NotCashAffecting:
                        break;
                    case CashImpact.Unknown:
                        return ReconciliationNeeded(CashStateReason.AmbiguousTransaction);
                    case CashImpact.Correction:
                        return ReconciliationNeeded(CashStateReason.Correction);
                    case CashImpact.Unsupported:
                        return ReconciliationNeeded(CashStateReason.UnsupportedActivity);
                    default:
                        return ReconciliationNeeded(CashStateReason.AmbiguousTransaction);
                }
            }

            if (openingCashBalance is null)
            {
                return new CashState(
                    CashStateStatus.Unknown,
                    CashStateReason.MissingBaseline,
                    OpeningCashBalance: null,
                    CashBalance: null,
                    Difference: null);
            }

            var opening = openingCashBalance.Value;
            var balance = checked(opening + movement);
            return new CashState(CashStateStatus.Known, CashStateReason.None, opening, balance, movement);
        }
        catch (OverflowException)
        {
            return ReconciliationNeeded(CashStateReason.ArithmeticOverflow);
        }
    }

    private static CashState ReconciliationNeeded(CashStateReason reason) =>
        new(CashStateStatus.ReconciliationNeeded, reason, OpeningCashBalance: null, CashBalance: null, Difference: null);
}
