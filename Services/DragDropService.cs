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
    /// <summary>Folders deeper than this below a dropped folder are not searched.</summary>
    private const int MaxDepth = 32;

    /// <summary>
    /// One folder level: hidden and system entries are skipped (as Explorer hides them), and so are the
    /// entries that cannot be read.
    /// </summary>
    private static readonly EnumerationOptions OneLevel = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
    };

    /// <summary>
    /// Every .reg file named by <paramref name="droppedPaths"/>, in order, each once. A path that cannot
    /// be read (a folder whose listing fails, a file that cannot be opened) is added to
    /// <paramref name="unreadable"/> with the reason, and the others are still resolved.
    /// </summary>
    public List<string> ResolveDroppedPaths(IEnumerable<string> droppedPaths, ICollection<string>? unreadable = null)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string file)
        {
            if (seen.Add(Path.GetFullPath(file))) result.Add(file);
        }

        foreach (var path in droppedPaths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;

            try
            {
                if (Directory.Exists(path))
                {
                    // The folder itself must be readable (IgnoreInaccessible would hide that it is not).
                    using (var probe = Directory.EnumerateFileSystemEntries(path).GetEnumerator()) probe.MoveNext();
                    ScanFolder(path, 0, Add);
                    continue;
                }

                if (!File.Exists(path))
                {
                    unreadable?.Add($"{path} (not found)");
                    continue;
                }

                if (path.EndsWith(".reg", StringComparison.OrdinalIgnoreCase))
                {
                    Add(path);
                    continue;
                }

                if (!Path.HasExtension(path) && RegFileParser.HasRegHeader(path)) Add(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                unreadable?.Add($"{path} ({ex.Message})");
            }
        }
        return result;
    }

    /// <summary>
    /// The .reg files of <paramref name="folder"/> and of its subfolders, up to <see cref="MaxDepth"/>
    /// levels. A junction or a symbolic link to a folder is not followed: one that points back to a
    /// parent would list the same files again and again (a cloud placeholder folder, which is no link,
    /// is searched). A subfolder that cannot be read is skipped.
    /// </summary>
    private static void ScanFolder(string folder, int depth, Action<string> add)
    {
        foreach (var file in Directory.EnumerateFiles(folder, "*.reg", OneLevel)) add(file);
        if (depth >= MaxDepth) return;

        foreach (var sub in new DirectoryInfo(folder).EnumerateDirectories("*", OneLevel))
        {
            try
            {
                if (sub.LinkTarget is not null) continue;
                ScanFolder(sub.FullName, depth + 1, add);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // A subfolder that disappeared or cannot be read: the others are still searched.
            }
        }
    }
}
