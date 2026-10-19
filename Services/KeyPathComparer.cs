// Order of registry key paths, as a tree lists them.

using System;
using System.Collections.Generic;

namespace RegCompare.Services;

/// <summary>
/// Orders registry key paths as a tree lists them: level by level, each level's names without regard
/// to case (<c>A</c>, <c>A\X</c>, <c>AB</c>). A plain ordinal comparison puts <c>AB</c> before
/// <c>A\X</c> (<c>\</c> sorts after the letters), so a key whose parent has no header of its own would
/// land before that parent's siblings.
/// </summary>
public sealed class KeyPathComparer : IComparer<string>
{
    public static KeyPathComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        var length = Math.Min(x.Length, y.Length);
        for (var i = 0; i < length; i++)
        {
            var a = x[i];
            var b = y[i];
            if (a == b) continue;
            // The end of a level comes before any character of a longer name.
            if (a == '\\') return -1;
            if (b == '\\') return 1;
            var order = char.ToUpperInvariant(a).CompareTo(char.ToUpperInvariant(b));
            if (order != 0) return order;
        }
        return x.Length.CompareTo(y.Length);
    }
}
