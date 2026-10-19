// One row of the values grid (the bottom half of each column).

using RegCompare.Models;
using RegCompare.Services;

namespace RegCompare.ViewModels;

/// <summary>
/// One row of the values grid: name + type + display value, plus the diff status that
/// drives the row color (red / olive / blue) and font weight.
/// </summary>
public sealed class ValueRowViewModel
{
    /// <summary>The value's name in the file, empty for the default value (<c>@</c>).</summary>
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required string DisplayValue { get; init; }

    public required DiffStatus Status { get; init; }

    /// <summary>True when this row should be highlighted (any non-identical, non-reference, non-hidden status).</summary>
    public required bool IsChange { get; init; }

    /// <summary>The name as the grid shows it: <c>(Default)</c> for the default value.</summary>
    public string DisplayName => DisplayNameOf(Name);

    /// <summary>
    /// The name of a value as the grids show it, as Registry Editor does: <c>(Default)</c> for the
    /// default value, the others without their .reg escapes (<c>C:\\Windows</c> reads <c>C:\Windows</c>).
    /// </summary>
    public static string DisplayNameOf(string name) => name.Length == 0 ? "(Default)" : RegTypeFormatter.Unescape(name);
}
