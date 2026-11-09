using FlushedYo.Plugins.Fidelity;

namespace FlushedYo.Plugins.Fidelity.Tests.Support;

/// <summary>
/// Builds synthetic Fidelity transaction exports. Account numbers are fake, and the masked key is
/// derived through the production <see cref="InstitutionIdentity"/> so fixtures never carry real data.
/// </summary>
internal static class FidelityCsvFixture
{
    public const string Header =
        "Run Date,Action,Symbol,Description,Account Number,Amount $,Quantity";

    /// <summary>Masks a fake raw account number exactly as production does.</summary>
    public static string AccountKey(string rawAccountNumber) =>
        InstitutionIdentity.Fidelity.MaskAccount(rawAccountNumber);

    /// <summary>Optional non-default column values for <see cref="Row"/>.</summary>
    public sealed record RowOptions
    {
        /// <summary>Description column value.</summary>
        public string Description { get; init; } = "Synthetic activity";

        /// <summary>Symbol column value.</summary>
        public string Symbol { get; init; } = "TEST";

        /// <summary>Action column value.</summary>
        public string Action { get; init; } = "Buy";
    }

    /// <summary>One signed CSV row.</summary>
    public static string Row(
        string rawAccountNumber,
        string date,
        string amount,
        RowOptions? options = null)
    {
        var columns = options ?? new RowOptions();
        return string.Join(
            ',',
            date,
            columns.Action,
            columns.Symbol,
            columns.Description,
            rawAccountNumber,
            amount,
            "1");
    }

    /// <summary>An export with a header and the given rows.</summary>
    public static string Export(params string[] rows) =>
        string.Join('\n', [Header, .. rows]) + "\n";
}
