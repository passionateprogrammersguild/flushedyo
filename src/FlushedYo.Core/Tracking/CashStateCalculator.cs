using System.Collections.Frozen;
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
    /// <param name="hasRejectedTransactions">Whether the import also contained invalid downloaded records.</param>
    /// <returns>
    /// A known cash state only when all inputs are explicit. Missing baselines produce an unknown state;
    /// rejected, ambiguous, correction, and unsupported activity produce reconciliation-needed states.
    /// Decimal overflow also produces a reconciliation-needed state. Rejected records have first precedence
    /// and map to <see cref="CashStateReason.AmbiguousTransaction"/>; otherwise the first unresolved row or
    /// arithmetic failure encountered wins, and a missing baseline is considered only after all rows. Unknown
    /// and reconciliation-needed states never contain fabricated balances or differences.
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

        if (TryAccumulateMovement(newlyInsertedTransactions, out var movement, out var failure))
        {
            return Complete(openingCashBalance, movement);
        }

        return ReconciliationNeeded(failure);
    }

    private static bool TryAccumulateMovement(
        IEnumerable<AccountTransaction> newlyInsertedTransactions,
        out decimal movement,
        out CashStateReason failure)
    {
        movement = 0m;
        try
        {
            foreach (var accountTransaction in newlyInsertedTransactions)
            {
                ArgumentNullException.ThrowIfNull(accountTransaction);

                // Fail closed: an unclassified impact must never contribute cash.
                var (contributesCash, unresolvedReason) = ImpactRules.GetValueOrDefault(
                    accountTransaction.CashImpact,
                    (ContributesCash: false, UnresolvedReason: CashStateReason.AmbiguousTransaction));
                if (unresolvedReason is { } reason)
                {
                    failure = reason;
                    return false;
                }

                movement = checked(movement + (contributesCash ? accountTransaction.Transaction.Amount : 0m));
            }

            failure = CashStateReason.None;
            return true;
        }
        catch (OverflowException)
        {
            failure = CashStateReason.ArithmeticOverflow;
            return false;
        }
    }

    private static CashState Complete(decimal? openingCashBalance, decimal movement)
    {
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
        try
        {
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

    /// <summary>Per-impact rule: whether the amount contributes cash and which unresolved reason, if any, applies.</summary>
    private static readonly FrozenDictionary<CashImpact, (bool ContributesCash, CashStateReason? UnresolvedReason)> ImpactRules =
        new Dictionary<CashImpact, (bool, CashStateReason?)>
        {
            [CashImpact.CashAffecting] = (true, null),
            [CashImpact.NotCashAffecting] = (false, null),
            [CashImpact.Unknown] = (false, CashStateReason.AmbiguousTransaction),
            [CashImpact.Correction] = (false, CashStateReason.Correction),
            [CashImpact.Unsupported] = (false, CashStateReason.UnsupportedActivity),
        }.ToFrozenDictionary();
}
