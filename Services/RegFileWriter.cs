// Generates the textual content of a .reg file from a partial selection inside a parsed RegFile.
// Used by the "Export..." context menu items in both the keys tree and the values grid.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RegCompare.Models;

namespace RegCompare.Services;

/// <summary>
/// Builds the textual content of a .reg file in the format of the file it comes from: Windows
/// Registry Editor version 5 (UTF-16LE), or <c>REGEDIT4</c> (ANSI) for a REGEDIT4 file, whose
/// EXPAND_SZ and MULTI_SZ data are ANSI bytes.
/// </summary>
/// <remarks>
/// The user can export either:
/// <list type="bullet">
/// <item>A subset of values under a single key (Values-grid "Export..."), or</item>
/// <item>A whole subtree starting at a single key (Keys-tree "Export..." with all descendant keys + values).</item>
/// </list>
/// In both cases the output declares every parent key as an empty header (no values),
/// preserving the path hierarchy without leaking sibling subkeys' values into the export. Key and
/// value names are written with the file's own spelling, data exactly as read.
/// </remarks>
public static class RegFileWriter
{
    /// <summary>
    /// The bytes of an export of <paramref name="file"/>: UTF-16LE with its byte-order mark, as Registry
    /// Editor writes version 5 files; for a REGEDIT4 file, the ANSI code page without any mark (Registry
    /// Editor reads REGEDIT4 files as ANSI). A character that code page cannot write throws an
    /// <see cref="EncoderFallbackException"/> rather than being replaced by a look-alike.
    /// </summary>
    public static byte[] Encode(RegFile file, string content)
    {
        if (!file.IsRegedit4) return [.. Encoding.Unicode.Preamble, .. Encoding.Unicode.GetBytes(content)];
        var ansi = Encoding.GetEncoding(AnsiEncoding.Current.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ReplacementFallback);
        return ansi.GetBytes(content);
    }

    /// <summary>
    /// Build the .reg content for a subset of values under a single key.
    /// Includes parent-key declarations (empty headers) up to the root.
    /// </summary>
    public static string BuildFromValues(RegFile file, string keyPath, IEnumerable<string> valueNames)
    {
        var key = Spelling(file, keyPath);
        var sb = NewBuilder(file);

        foreach (var parentPath in EnumerateParentChain(key))
        {
            AppendKeyHeader(sb, parentPath);
        }

        AppendKeyHeader(sb, key);
        if (file.Data.TryGetValue(key, out var keyValues))
        {
            foreach (var name in valueNames)
            {
                // The file's own spelling of the name (the columns may show another file's).
                var stored = keyValues.Keys.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
                if (stored is not null) AppendValueLine(sb, stored, keyValues[stored]);
            }
        }
        sb.AppendLine();

        return sb.ToString();
    }

    /// <summary>True when <paramref name="file"/> has <paramref name="rootKey"/> or a key below it:
    /// a subtree export would then hold something.</summary>
    public static bool HasSubtree(RegFile file, string rootKey) => SubtreeKeys(file, rootKey).Any();

    /// <summary>
    /// Build the .reg content for a subtree starting at <paramref name="rootKey"/>:
    /// the key itself with all its values, plus every descendant key (recursively) with all
    /// its values. Includes parent-key declarations (empty headers) up to the registry root.
    /// </summary>
    public static string BuildFromSubtree(RegFile file, string rootKey)
    {
        var sb = NewBuilder(file);

        foreach (var parentPath in EnumerateParentChain(Spelling(file, rootKey)))
        {
            AppendKeyHeader(sb, parentPath);
        }

        foreach (var key in SubtreeKeys(file, rootKey).OrderBy(static k => k, KeyPathComparer.Instance))
        {
            AppendKeyHeader(sb, key);
            foreach (var (name, raw) in file.Data[key].OrderBy(static kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
            {
                AppendValueLine(sb, name, raw);
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>The keys of <paramref name="file"/> that are <paramref name="rootKey"/> or below it.</summary>
    private static IEnumerable<string> SubtreeKeys(RegFile file, string rootKey)
    {
        var prefix = rootKey + "\\";
        return file.Data.Keys.Where(k => string.Equals(k, rootKey, StringComparison.OrdinalIgnoreCase)
                                      || k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// <paramref name="path"/> as <paramref name="file"/> writes it: its own key, or the start of one
    /// of its keys below that path (a path level without a header of its own); else as given.
    /// </summary>
    private static string Spelling(RegFile file, string path)
    {
        var prefix = path + "\\";
        string? below = null;
        foreach (var key in file.Data.Keys)
        {
            if (string.Equals(key, path, StringComparison.OrdinalIgnoreCase)) return key;
            if (below is null && key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) below = key[..path.Length];
        }
        return below ?? path;
    }

    private static StringBuilder NewBuilder(RegFile file)
    {
        var sb = new StringBuilder();
        sb.AppendLine(file.IsRegedit4 ? "REGEDIT4" : "Windows Registry Editor Version 5.00");
        sb.AppendLine();
        return sb;
    }

    private static void AppendKeyHeader(StringBuilder sb, string keyPath)
    {
        sb.Append('[').Append(keyPath).Append(']').AppendLine();
    }

    private static void AppendValueLine(StringBuilder sb, string name, string rawValue)
    {
        // "@" is the default value, whose name is empty; names keep their .reg escapes.
        if (name.Length == 0)
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
    /// None for a deletion entry (<c>[-HKEY_…]</c>): its parents would be deletions too, and a
    /// deletion needs no parent header.
    /// </summary>
    private static IEnumerable<string> EnumerateParentChain(string keyPath)
    {
        if (keyPath.StartsWith('-')) yield break;

        var parts = keyPath.Split('\\');
        var path = "";
        for (var i = 0; i < parts.Length - 1; i++)
        {
            path = i == 0 ? parts[0] : path + "\\" + parts[i];
            yield return path;
        }
    }
}
