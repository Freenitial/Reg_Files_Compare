// In-memory representation of a parsed .reg file plus its filesystem metadata.

using System.Collections.Generic;

namespace RegCompare.Models;

/// <summary>
/// A loaded .reg file: filesystem metadata plus the parsed content as a nested dictionary
/// (<c>keyPath -&gt; valueName -&gt; rawValue</c>).
/// </summary>
public sealed class RegFile
{
    /// <summary>Absolute path of the file on disk.</summary>
    public required string Path { get; init; }

    /// <summary>File name only, derived from <see cref="Path"/>.</summary>
    public required string FileName { get; init; }

    /// <summary>Last write time of the file at load time.</summary>
    public required System.DateTime Modified { get; init; }

    /// <summary>File size in bytes at load time.</summary>
    public required long SizeBytes { get; init; }

    /// <summary>
    /// Parsed content. The outer key is the full registry key path (without surrounding brackets).
    /// The inner key is the value name as written in the file (escapes kept), the empty string for
    /// the default value (<c>@</c>), as in the registry itself. The inner value is the raw textual
    /// representation as written in the .reg file (right-hand side of the <c>=</c>).
    /// </summary>
    public required Dictionary<string, Dictionary<string, string>> Data { get; init; }

    /// <summary>
    /// True for a <c>REGEDIT4</c> file (its first line): its text is ANSI, and its <c>hex(2)</c> and
    /// <c>hex(7)</c> data hold ANSI bytes instead of UTF-16.
    /// </summary>
    public bool IsRegedit4 { get; init; }

    /// <summary>
    /// More of the file's lines end with \n alone than with \r\n (its line endings were converted, by git or
    /// an editor; the line breaks inside strings are data, not counted). reg.exe
    /// writes each line break of a string's data as \r\n: in such a file the conversion gave the data
    /// back as it is, where a file ending its lines with \r\n holds each \n of the data as \r\n.
    /// </summary>
    public bool LfLines { get; init; }

    /// <summary>Human-friendly file size (B / KB / MB).</summary>
    public string DisplaySize
    {
        get
        {
            const long Kb = 1024;
            const long Mb = Kb * 1024;
            if (SizeBytes < Kb) return $"{SizeBytes} B";
            if (SizeBytes < Mb) return $"{SizeBytes / (double)Kb:0.##} KB";
            return $"{SizeBytes / (double)Mb:0.##} MB";
        }
    }
}
