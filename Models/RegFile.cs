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
    /// The inner key is the value name (with <c>(Default)</c> for the <c>@</c> entry).
    /// The inner value is the raw textual representation as written in the .reg file (right-hand side of the <c>=</c>).
    /// </summary>
    public required Dictionary<string, Dictionary<string, string>> Data { get; init; }

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
