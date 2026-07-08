// Diff status used by DiffEngine to label each registry key/value vs the reference file.

namespace RegCompare.Models;

/// <summary>
/// Result of a per-key or per-value diff against the reference file.
/// </summary>
public enum DiffStatus
{
    /// <summary>The item belongs to the reference file itself.</summary>
    Reference,

    /// <summary>The item exists in the reference file but is missing from the current file.</summary>
    Missing,

    /// <summary>The item exists in the current file but is absent from the reference file.</summary>
    Added,

    /// <summary>The item exists in both files but with a different value.</summary>
    Different,

    /// <summary>The item exists in both files with the same value.</summary>
    Identical,

    /// <summary>The key exists in both files (key-level diff has no value comparison).</summary>
    Exists,

    /// <summary>The item exists in neither file (used to drop irrelevant rows from rendering).</summary>
    Hidden,
}
