// Names that tell files apart when several share one.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RegCompare.Services;

/// <summary>The names files are shown by in titles and lists.</summary>
public static class FileNames
{
    /// <summary>
    /// Each file's name; for files that share it, followed by as much of their folder as tells them
    /// apart: <c>a.reg (x)</c> and <c>a.reg (y)</c>, <c>a.reg (C:\x)</c> and <c>a.reg (D:\x)</c>,
    /// <c>a.reg (C:\)</c> for a drive root.
    /// </summary>
    public static List<string> Distinct(IReadOnlyList<string> paths)
    {
        var names = paths.Select(static p => Path.GetFileName(p)).ToList();
        var shared = Enumerable.Range(0, paths.Count)
            .GroupBy(i => names[i], StringComparer.OrdinalIgnoreCase)
            .Where(static g => g.Count() > 1);
        foreach (var group in shared)
        {
            var members = group.ToList();
            for (var depth = 1; ; depth++)
            {
                var tails = members.Select(i => FolderTail(paths[i], depth, out _)).ToList();
                var whole = members.All(i => { FolderTail(paths[i], depth, out var all); return all; });
                if (whole || tails.Distinct(StringComparer.OrdinalIgnoreCase).Count() == tails.Count)
                {
                    for (var k = 0; k < members.Count; k++) names[members[k]] = $"{names[members[k]]} ({tails[k]})";
                    break;
                }
            }
        }
        return names;
    }

    /// <summary>
    /// The last <paramref name="depth"/> folders of the path's folder, or the whole folder (its root
    /// included) when it has no more (<paramref name="whole"/>).
    /// </summary>
    private static string FolderTail(string path, int depth, out bool whole)
    {
        var folder = Path.GetDirectoryName(path) ?? "";
        var segments = folder.TrimEnd('\\').Split('\\');
        whole = depth >= segments.Length;
        return whole ? folder : string.Join('\\', segments[^depth..]);
    }
}
