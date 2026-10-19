// Resolves dropped paths (files / folders / extensionless files) into a flat list of .reg files to load.

using System;
using System.Collections.Generic;
using System.IO;

namespace RegCompare.Services;

/// <summary>
/// Stateless resolver that expands a list of dropped filesystem paths into a flat list of .reg files.
/// Folders are scanned recursively for <c>*.reg</c> files; a file without extension given directly
/// is included only when its first line is a .reg header.
/// </summary>
public sealed class DragDropService
{
    /// <summary>
    /// Every depth up to 32 levels (a junction that points back to a parent would otherwise loop);
    /// hidden and system entries are skipped (as Explorer hides them: the junctions of the user
    /// profile among them), and so are the subfolders that cannot be read.
    /// </summary>
    private static readonly EnumerationOptions FolderScan = new()
    {
        RecurseSubdirectories = true,
        MaxRecursionDepth = 32,
        IgnoreInaccessible = true,
    };

    /// <summary>
    /// Every .reg file named by <paramref name="droppedPaths"/>, in order. A path that cannot be read
    /// (a folder whose listing fails, a file that cannot be opened) is added to
    /// <paramref name="unreadable"/> with the reason, and the others are still resolved.
    /// </summary>
    public List<string> ResolveDroppedPaths(IEnumerable<string> droppedPaths, ICollection<string>? unreadable = null)
    {
        var result = new List<string>();
        foreach (var path in droppedPaths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;

            try
            {
                if (Directory.Exists(path))
                {
                    // The folder itself must be readable (IgnoreInaccessible would hide that it is not).
                    using (var probe = Directory.EnumerateFileSystemEntries(path).GetEnumerator()) probe.MoveNext();
                    result.AddRange(Directory.EnumerateFiles(path, "*.reg", FolderScan));
                    continue;
                }

                if (!File.Exists(path)) continue;

                if (path.EndsWith(".reg", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(path);
                    continue;
                }

                if (!Path.HasExtension(path) && RegFileParser.HasRegHeader(path)) result.Add(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                unreadable?.Add($"{path} ({ex.Message})");
            }
        }
        return result;
    }
}
