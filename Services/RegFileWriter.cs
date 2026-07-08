// Generates the textual content of a .reg file from a partial selection inside a parsed RegFile.
// Used by the "Export..." context menu items in both the keys tree and the values grid.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RegCompare.Models;

namespace RegCompare.Services;

/// <summary>
/// Builds the textual content of a Windows-Registry-Editor v5 .reg file.
/// </summary>
/// <remarks>
/// The user can export either:
/// <list type="bullet">
/// <item>A subset of values under a single key (Values-grid "Export..."), or</item>
/// <item>A whole subtree starting at a single key (Keys-tree "Export..." with all descendant keys + values).</item>
/// </list>
/// In both cases the output declares every parent key as an empty header (no values),
/// preserving the path hierarchy without leaking sibling subkeys' values into the export.
/// </remarks>
public static class RegFileWriter
{
    /// <summary>
    /// Build the .reg content for a subset of values under a single key.
    /// Includes parent-key declarations (empty headers) up to the root.
    /// </summary>
    public static string BuildFromValues(RegFile file, string keyPath, IEnumerable<string> valueNames)
    {
        var values = valueNames.ToList();
        var sb = NewBuilder();

        foreach (var parentPath in EnumerateParentChain(keyPath, includeSelf: false))
        {
            AppendKeyHeader(sb, parentPath);
        }

        AppendKeyHeader(sb, keyPath);
        if (file.Data.TryGetValue(keyPath, out var keyValues))
        {
            foreach (var name in values)
            {
                if (keyValues.TryGetValue(name, out var raw))
                {
                    AppendValueLine(sb, name, raw);
                }
            }
        }
        sb.AppendLine();

        return sb.ToString();
    }

    /// <summary>
    /// Build the .reg content for a subtree starting at <paramref name="rootKey"/>:
    /// the key itself with all its values, plus every descendant key (recursively) with all
    /// its values. Includes parent-key declarations (empty headers) up to the registry root.
    /// </summary>
    public static string BuildFromSubtree(RegFile file, string rootKey)
    {
        var sb = NewBuilder();

        foreach (var parentPath in EnumerateParentChain(rootKey, includeSelf: false))
        {
            AppendKeyHeader(sb, parentPath);
        }

        var prefix = rootKey + "\\";
        var subtreeKeys = file.Data.Keys
            .Where(k => string.Equals(k, rootKey, StringComparison.OrdinalIgnoreCase)
                     || k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var key in subtreeKeys)
        {
            AppendKeyHeader(sb, key);
            if (file.Data.TryGetValue(key, out var values))
            {
                foreach (var (name, raw) in values.OrderBy(static kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
                {
                    AppendValueLine(sb, name, raw);
                }
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static StringBuilder NewBuilder()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Windows Registry Editor Version 5.00");
        sb.AppendLine();
        return sb;
    }

    private static void AppendKeyHeader(StringBuilder sb, string keyPath)
    {
        sb.Append('[').Append(keyPath).Append(']').AppendLine();
    }

    private static void AppendValueLine(StringBuilder sb, string name, string rawValue)
    {
        // "@" represents the (Default) value name in .reg syntax.
        if (string.Equals(name, "(Default)", StringComparison.Ordinal))
        {
            sb.Append('@');
        }
        else
        {
            sb.Append('"').Append(name).Append('"');
        }
        sb.Append('=').Append(rawValue).AppendLine();
    }

    /// <summary>
    /// Yield every parent key path of <paramref name="keyPath"/>, from the registry root down.
    /// When <paramref name="includeSelf"/> is true, <paramref name="keyPath"/> itself is yielded last.
    /// </summary>
    private static IEnumerable<string> EnumerateParentChain(string keyPath, bool includeSelf)
    {
        var parts = keyPath.Split('\\');
        var path = "";
        for (var i = 0; i < parts.Length; i++)
        {
            path = i == 0 ? parts[0] : path + "\\" + parts[i];
            if (i < parts.Length - 1 || includeSelf)
            {
                yield return path;
            }
        }
    }
}
