// One row of the values grid (the bottom half of each column).

using RegCompare.Models;

namespace RegCompare.ViewModels;

/// <summary>
/// One row of the values DataGrid: name + type + display value, plus the diff status that
/// drives the row color (red / olive / blue) and font weight.
/// </summary>
public sealed class ValueRowViewModel
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required string DisplayValue { get; init; }

    /// <summary>
    /// Raw .reg-format value (e.g. <c>"foo"</c>, <c>dword:0000ffff</c>, <c>hex(7):...</c>).
    /// Kept alongside <see cref="DisplayValue"/> so the export-to-.reg feature can write the
    /// unmodified raw form, while the grid still shows the human-readable decoded form.
    /// Empty when the row exists in the diff but the underlying file does not have this value.
    /// </summary>
    public required string RawValue { get; init; }

    public required DiffStatus Status { get; init; }

    /// <summary>True when this row should be highlighted (any non-identical, non-reference, non-hidden status).</summary>
    public required bool IsChange { get; init; }
}
