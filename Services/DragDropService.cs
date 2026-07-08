// Resolves dropped paths (files / folders / extensionless files) into a flat list of .reg files to load.

using System;
using System.Collections.Generic;
using System.IO;

namespace RegCompare.Services;

/// <summary>
/// Stateless resolver that expands a list of dropped filesystem paths into a flat list of .reg files.
/// Folders are scanned recursively; extension-less files are included only when the first line
/// starts with <c>Windows Registry Editor</c>.
/// </summary>
public sealed class DragDropService
{
    /// <summary>
    /// Walk every dropped path and yield the absolute path of each .reg file to load.
    /// </summary>
    public IEnumerable<string> ResolveDroppedPaths(IEnumerable<string> droppedPaths)
    {
        foreach (var path in droppedPaths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;

            if (Directory.Exists(path))
            {
                IEnumerable<string> regs;
                try
                {
                    regs = Directory.EnumerateFiles(path, "*.reg", SearchOption.AllDirectories);
                }
                catch
                {
                    continue;
                }
                foreach (var file in regs) yield return file;
                continue;
            }

            if (!File.Exists(path)) continue;

            if (path.EndsWith(".reg", StringComparison.OrdinalIgnoreCase))
            {
                yield return path;
                continue;
            }

            if (Path.HasExtension(path)) continue;

            string? firstLine;
            try
            {
                using var reader = new StreamReader(path);
                firstLine = reader.ReadLine();
            }
            catch
            {
                continue;
            }

            if (firstLine is not null && firstLine.StartsWith("Windows Registry Editor", StringComparison.OrdinalIgnoreCase))
            {
                yield return path;
            }
        }
    }
}
