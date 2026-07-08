// Lightweight value record passed from the diff engine to the values grid.

namespace RegCompare.Models;

/// <summary>
/// A single registry value as displayed in the values grid (Name / Type / Data columns).
/// Created on demand from raw .reg strings; not stored in the parsed file dictionary.
/// </summary>
/// <param name="Name">Value name, with <c>(Default)</c> substituted for the <c>@</c> entry.</param>
/// <param name="Type">Inferred Windows registry type.</param>
/// <param name="DisplayValue">Human-readable value string (quotes stripped, hex prefixed with <c>0x</c>).</param>
public readonly record struct RegValue(string Name, RegValueType Type, string DisplayValue);
