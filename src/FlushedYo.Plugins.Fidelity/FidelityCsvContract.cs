namespace FlushedYo.Plugins.Fidelity;

/// <summary>
/// Fidelity-specific CSV dialect: the column header names and formats the institution's
/// transaction export uses. Values are compared case-insensitively after trimming.
/// </summary>
public sealed record FidelityCsvContract
{
    /// <summary>Header of the run date column.</summary>
    public const string RunDate = "Run Date";

    /// <summary>Header of the action/date column used when the export omits Run Date.</summary>
    public const string ActionDate = "Action Date";

    /// <summary>Header of the transaction action column, for example <c>Buy</c>.</summary>
    public const string Action = "Action";

    /// <summary>Header of the security/memo column that names the transaction.</summary>
    public const string Name = "Name";

    /// <summary>Header of the security description column.</summary>
    public const string Description = "Description";

    /// <summary>Header of the symbol column, present on security rows.</summary>
    public const string Symbol = "Symbol";

    /// <summary>Header of the account-number column used for account-scoped identities.</summary>
    public const string AccountNumber = "Account Number";

    /// <summary>Header of the amount column, signed in the institution's dialect.</summary>
    public const string Amount = "Amount $";

    /// <summary>Header of the quantity column.</summary>
    public const string Quantity = "Quantity";

    /// <summary>Key for the shared position that prefers this contract's headers, in catalog order.</summary>
    public static readonly string[] DateHeaderPreference = [RunDate, ActionDate];
}
