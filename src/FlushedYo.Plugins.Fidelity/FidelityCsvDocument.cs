namespace FlushedYo.Plugins.Fidelity;

/// <summary>
/// A parsed CSV export: the header column map plus the raw records that follow it, in file order.
/// Blank records are kept so reported line numbers stay aligned with the file.
/// </summary>
internal sealed class FidelityCsvDocument(string[] records, Dictionary<string, int> header)
{
    /// <summary>The header column map, names compared case-insensitively.</summary>
    public Dictionary<string, int> Header { get; } = header;

    /// <summary>The number of records after the header line.</summary>
    public int RecordCount => records.Length;

    /// <summary>
    /// Splits an artifact into records and parses its header.
    /// </summary>
    /// <param name="csv">The artifact contents as text, decoded by the caller.</param>
    /// <returns>The parsed document.</returns>
    /// <exception cref="FidelityAdapterException">
    /// The artifact has no header row or the header lacks the required amount column.
    /// </exception>
    public static FidelityCsvDocument Parse(string csv)
    {
        var records = SplitRecords(csv);
        if (records.Length == 0 || string.IsNullOrWhiteSpace(records[0]))
        {
            throw new FidelityAdapterException("The export artifact contains no header row.");
        }

        var header = ParseHeader(records[0]);
        if (!header.ContainsKey(FidelityCsvContract.Amount))
        {
            throw new FidelityAdapterException("The export artifact header lacks the required amount column.");
        }

        return new FidelityCsvDocument(records[1..], header);
    }

    /// <summary>Whether the record at <paramref name="index"/> is blank and must be skipped.</summary>
    public bool IsBlank(int index) => string.IsNullOrWhiteSpace(records[index]);

    /// <summary>Splits the record at <paramref name="index"/> into its fields.</summary>
    public string[] SplitRecord(int index) => FidelityCsvFields.Split(records[index]);

    private static Dictionary<string, int> ParseHeader(string headerLine)
    {
        var cells = FidelityCsvFields.Split(headerLine);
        var header = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < cells.Length; index++)
        {
            var name = cells[index]?.Trim();
            if (!string.IsNullOrEmpty(name) && !header.ContainsKey(name))
            {
                header[name] = index;
            }
        }

        return header;
    }

    private static string[] SplitRecords(string csv) =>
        csv.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
}
