using System.Text;

namespace FlushedYo.Plugins.Fidelity;

/// <summary>
/// Splits one CSV line into fields with RFC 4180 quoting: a doubled quote is a literal quote and a
/// quoted field may contain commas. The splitter is line-based; embedded newlines are not part of the
/// Fidelity export contract, so a record is always one line.
/// </summary>
public static class FidelityCsvFields
{
    /// <summary>Splits one line into its fields, preserving empty fields.</summary>
    /// <param name="line">The raw CSV line.</param>
    /// <returns>One entry per field, in order.</returns>
    public static string[] Split(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var cells = new List<string>();
        var field = new StringBuilder();
        var position = 0;
        while (position < line.Length)
        {
            position = Consume(line, position, field, cells);
        }

        cells.Add(field.ToString());
        return [.. cells];
    }

    private static int Consume(string line, int position, StringBuilder field, List<string> cells)
    {
        var current = line[position];
        if (current == '"')
        {
            return ConsumeQuoted(line, position + 1, field);
        }

        if (current == ',')
        {
            cells.Add(field.ToString());
            field.Clear();
            return position + 1;
        }

        field.Append(current);
        return position + 1;
    }

    private static int ConsumeQuoted(string line, int position, StringBuilder field)
    {
        while (position < line.Length)
        {
            var current = line[position];
            if (current != '"')
            {
                field.Append(current);
                position++;
                continue;
            }

            if (position + 1 < line.Length && line[position + 1] == '"')
            {
                field.Append('"');
                position += 2;
                continue;
            }

            return position + 1;
        }

        return position;
    }
}
