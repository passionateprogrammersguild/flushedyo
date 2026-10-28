using System.Text;

namespace FlushedYo.Core.Commands;

/// <summary>Renders an aligned plain-text table.</summary>
public static class TextTable
{
    private const string ColumnSeparator = "  ";

    /// <summary>Renders <paramref name="headers"/> and <paramref name="rows"/> as a table.</summary>
    /// <param name="headers">Column headers.</param>
    /// <param name="rows">Data rows; each must have exactly one non-null cell per header. Use <c>-</c> for empty values.</param>
    /// <returns>
    /// The header row followed by the data rows, cells left-aligned to the longest cell in each column.
    /// The last column is not padded, lines are joined with LF, and there is no trailing newline or whitespace.
    /// </returns>
    /// <exception cref="ArgumentException">A row has the wrong number of cells, or a cell is null.</exception>
    public static string Render(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);

        var columns = headers.Count;
        var widths = new int[columns];
        for (var column = 0; column < columns; column++)
        {
            widths[column] = Cell(headers[column]).Length;
        }

        foreach (var row in rows)
        {
            if (row is null || row.Count != columns)
            {
                throw new ArgumentException($"Every row must have exactly {columns} cells.", nameof(rows));
            }

            for (var column = 0; column < columns; column++)
            {
                widths[column] = Math.Max(widths[column], Cell(row[column]).Length);
            }
        }

        var builder = new StringBuilder();
        AppendLine(builder, headers, widths);
        foreach (var row in rows)
        {
            builder.Append('\n');
            AppendLine(builder, row, widths);
        }

        return builder.ToString();
    }

    private static void AppendLine(StringBuilder builder, IReadOnlyList<string> cells, int[] widths)
    {
        var last = widths.Length - 1;
        for (var column = 0; column < last; column++)
        {
            builder.Append(Cell(cells[column]).PadRight(widths[column])).Append(ColumnSeparator);
        }

        builder.Append(Cell(cells[last]));
    }

    private static string Cell(string? value) =>
        value ?? throw new ArgumentException("Table cells must not be null.", nameof(value));
}
