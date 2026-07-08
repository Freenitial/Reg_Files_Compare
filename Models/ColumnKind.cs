// Discriminator between file columns (the loaded .reg files) and search-result columns.

namespace RegCompare.Models;

/// <summary>
/// Distinguishes the two kinds of columns rendered side-by-side in the main view.
/// </summary>
public enum ColumnKind
{
    /// <summary>A column bound to a single loaded .reg file.</summary>
    File,

    /// <summary>A column showing the matches of a search query across all checked files.</summary>
    Search,
}
