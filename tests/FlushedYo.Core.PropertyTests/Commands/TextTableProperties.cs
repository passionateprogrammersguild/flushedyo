using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using FlushedYo.Core.Commands;

namespace FlushedYo.Core.PropertyTests.Commands;

/// <summary>
/// Property family for plain-text table layout (spec: cli_shared_building_blocks).
/// For any well-formed headers/rows the renderer must emit LF-only lines with no trailing newline
/// or trailing whitespace, and every non-last column must be left-padded to exactly its maximum
/// width while each cell appears verbatim and in order. Golden outputs and boundary specs stay in
/// <c>TextTableTests</c>.
/// </summary>
public sealed class TextTableProperties
{
    // Structural invariants of the rendered table over generated well-formed input. Cells never
    // contain \r or \n (single-line values per the TextTable contract) and never end with a space,
    // so a well-formed render has no trailing whitespace on any line.
    [Property(MaxTest = 100)]
    public Property Render_MaintainsTheDocumentedStructuralInvariants() =>
        Prop.ForAll(Table().ToArbitrary(), table =>
        {
            var output = TextTable.Render(table.Headers, table.Rows);

            if (output.Contains('\r') || output.EndsWith('\n'))
            {
                return false;
            }

            var lines = output.Split('\n');
            if (lines.Length != table.Rows.Length + 1)
            {
                return false;
            }

            foreach (var line in lines)
            {
                if (line != line.TrimEnd())
                {
                    return false;
                }
            }

            var widths = ColumnWidths(table.Headers, table.Rows);
            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var cells = lineIndex == 0 ? table.Headers : table.Rows[lineIndex - 1];
                var line = lines[lineIndex];
                var offset = 0;

                for (var column = 0; column < table.Headers.Length; column++)
                {
                    // Non-last columns are left-padded to the column width; the last column is
                    // rendered unpadded (its width is the cell's own length).
                    var cellWidth = column < table.Headers.Length - 1 ? widths[column] : cells[column].Length;
                    if (offset + cellWidth > line.Length
                        || line.Substring(offset, cellWidth) != cells[column].PadRight(cellWidth))
                    {
                        return false;
                    }

                    offset += cellWidth;
                    if (column < table.Headers.Length - 1)
                    {
                        offset += 2; // the "  " column separator
                    }
                    else if (offset != line.Length)
                    {
                        // The last column is unpadded, so the cell must run to the end of the line.
                        return false;
                    }
                }
            }

            return true;
        });

    /// <summary>
    /// Well-formed input: 1..5 headers and 0..5 rows, every row the same length, non-null cells.
    /// Cells in the last column are non-empty (the documented <c>-</c> convention for empty values),
    /// so a rendered line always ends with visible content rather than the column separator.
    /// </summary>
    private static Gen<(string[] Headers, string[][] Rows)> Table() =>
        from headerCount in Gen.Choose(1, 5)
        from headers in NonEmptyLastColumnCells(headerCount)
        from rowCount in Gen.Choose(0, 5)
        from rows in Gen.ArrayOf(NonEmptyLastColumnCells(headerCount), rowCount)
        select (headers, rows);

    /// <summary>Any row (header or data) of <paramref name="count"/> cells with a non-empty last column.</summary>
    private static Gen<string[]> NonEmptyLastColumnCells(int count) =>
        from cells in Gen.ArrayOf(CellText(), count)
        from lastCell in CellText(allowEmpty: false)
        select cells[..^1].Append(lastCell).ToArray();

    /// <summary>
    /// A single-line cell that never ends with whitespace, so the unpadded last column can never
    /// leave trailing spaces. Empty cells are allowed except in the last column.
    /// </summary>
    private static Gen<string> CellText(bool allowEmpty = true) =>
        from wordCount in Gen.Choose(allowEmpty ? 0 : 1, 2)
        from words in Gen.ArrayOf(Gen.Elements(CellWords), wordCount)
        select string.Join(" ", words);

    private static readonly string[] CellWords = ["alpha", "b", "wider", "long", "x", "123", "a-b", "c/d", "0", "zz"];

    /// <summary>The documented column width: the longest header or cell in that column.</summary>
    private static int[] ColumnWidths(string[] headers, string[][] rows)
    {
        var widths = new int[headers.Length];
        for (var column = 0; column < headers.Length; column++)
        {
            var width = headers[column].Length;
            foreach (var row in rows)
            {
                width = Math.Max(width, row[column].Length);
            }

            widths[column] = width;
        }

        return widths;
    }
}
