// Text put on the clipboard by the values grids' Copy commands.

using System.Collections.Generic;
using System.Linq;
using System.Text;
using RegCompare.ViewModels;

namespace RegCompare.Services;

/// <summary>
/// The texts of the values grids' Copy Row(s), Copy Name and Copy Data. Rows are joined with line
/// feeds and the fields of a row with tabs, as spreadsheets paste them; a field holding a tab, a line
/// break or a double quote (a MULTI_SZ value, a string written on several lines) is put in double
/// quotes, with its own quotes doubled, so it stays in one cell.
/// </summary>
public static class ValueRowsText
{
    /// <summary>One line per row: name, type and data separated by tabs.</summary>
    public static string Rows(IReadOnlyList<ValueRowViewModel> rows)
        => string.Join('\n', rows.Select(static r => $"{Field(r.DisplayName)}\t{Field(r.Type)}\t{Field(r.DisplayValue)}"));

    /// <summary>The names, one per line; a single name is copied as it is.</summary>
    public static string Names(IReadOnlyList<ValueRowViewModel> rows) => List(rows.Select(static r => r.DisplayName).ToList());

    /// <summary>The data as displayed, one value per line; a single value is copied as it is.</summary>
    public static string Data(IReadOnlyList<ValueRowViewModel> rows) => List(rows.Select(static r => r.DisplayValue).ToList());

    private static string List(IReadOnlyList<string> items) => items.Count == 1 ? items[0] : string.Join('\n', items.Select(Field));

    /// <summary>A spreadsheet field: in double quotes (its own doubled) when it holds a tab, a line break or a quote.</summary>
    private static string Field(string text)
    {
        if (text.IndexOfAny(['\t', '\n', '\r', '"']) < 0) return text;
        var sb = new StringBuilder(text.Length + 2);
        sb.Append('"').Append(text.Replace("\"", "\"\"")).Append('"');
        return sb.ToString();
    }
}
