// Parser that turns a .reg file into the nested dictionary used by the diff engine.
// Handles the .reg backslash line-continuation used by long hex/multi-string values.

using System.Collections.Generic;
using System.IO;
using System.Text;
using RegCompare.Models;

namespace RegCompare.Services;

/// <summary>
/// Parses .reg files into <see cref="RegFile"/> instances.
/// Stateless and AOT-safe: no reflection, no regex, no globalization-sensitive operations.
/// </summary>
public sealed class RegFileParser
{
    /// <summary>
    /// Parse the .reg file at <paramref name="path"/>. Lines ending with a backslash
    /// (the .reg long-value continuation marker) are joined with the next line so that
    /// MULTI_SZ / EXPAND_SZ / BINARY values that wrap visually still produce a single
    /// raw value string.
    /// </summary>
    public RegFile Parse(string path)
    {
        var info = new FileInfo(path);
        var data = new Dictionary<string, Dictionary<string, string>>(System.StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? currentKey = null;

        string? pendingValueName = null;
        var pendingValueBuilder = new StringBuilder();

        using var reader = OpenReader(path);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var span = line.AsSpan().Trim();

            // If we are in the middle of a long value (previous line ended with '\'), this
            // line is a continuation. Append it (minus its own trailing '\' if any) and
            // either flush the value or stay in continuation mode.
            if (pendingValueName is not null)
            {
                var continuation = span;
                var stillContinues = continuation.Length > 0 && continuation[^1] == '\\';
                if (stillContinues) continuation = continuation[..^1].TrimEnd();
                pendingValueBuilder.Append(continuation);

                if (!stillContinues)
                {
                    currentKey![pendingValueName] = pendingValueBuilder.ToString();
                    pendingValueName = null;
                    pendingValueBuilder.Clear();
                }
                continue;
            }

            if (span.IsEmpty) continue;
            if (span[0] == ';') continue;

            if (span[0] == '[' && span[^1] == ']')
            {
                var keyPath = span[1..^1].ToString();
                if (!data.TryGetValue(keyPath, out currentKey))
                {
                    currentKey = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
                    data[keyPath] = currentKey;
                }
                continue;
            }

            if (currentKey is null) continue;

            // Quoted value names can contain '=' and escaped quotes, so the separator must be
            // located after the closing quote rather than at the first '=' of the line.
            string name;
            ReadOnlySpan<char> rawValue;
            if (span[0] == '"')
            {
                var close = FindClosingQuote(span);
                if (close < 0) continue;
                var rest = span[(close + 1)..];
                var eq = rest.IndexOf('=');
                if (eq < 0) continue;
                name = span[1..close].ToString();
                rawValue = rest[(eq + 1)..].Trim();
            }
            else
            {
                var eqIndex = span.IndexOf('=');
                if (eqIndex < 0) continue;
                var rawName = span[..eqIndex].Trim();
                name = rawName.Length == 1 && rawName[0] == '@'
                    ? "(Default)"
                    : rawName.Trim('"').ToString();
                rawValue = span[(eqIndex + 1)..].Trim();
            }

            // Long hex/multi-string values can span multiple physical lines when their last
            // character is a backslash. Open a continuation buffer and accumulate.
            if (rawValue.Length > 0 && rawValue[^1] == '\\')
            {
                pendingValueName = name;
                pendingValueBuilder.Clear();
                pendingValueBuilder.Append(rawValue[..^1].TrimEnd());
            }
            else
            {
                currentKey[name] = rawValue.ToString();
            }
        }

        // Flush any unterminated continuation (malformed file but be defensive).
        if (pendingValueName is not null && currentKey is not null)
        {
            currentKey[pendingValueName] = pendingValueBuilder.ToString();
        }

        return new RegFile
        {
            Path = path,
            FileName = System.IO.Path.GetFileName(path),
            Modified = info.LastWriteTime,
            SizeBytes = info.Length,
            Data = data,
        };
    }

    /// <summary>
    /// Open the file with encoding detection: BOM when present; otherwise NUL bytes near the
    /// start indicate UTF-16LE (regedit v5 exports without BOM); otherwise strict UTF-8 with a
    /// Latin-1 fallback for ANSI-era (REGEDIT4) files.
    /// </summary>
    private static TextReader OpenReader(string path)
    {
        var bytes = File.ReadAllBytes(path);

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return new StringReader(Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2));
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return new StringReader(Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2));
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new StringReader(Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));

        var probe = System.Math.Min(bytes.Length, 512);
        for (var i = 0; i < probe; i++)
        {
            if (bytes[i] == 0) return new StringReader(Encoding.Unicode.GetString(bytes));
        }

        try
        {
            return new StringReader(new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes));
        }
        catch (DecoderFallbackException)
        {
            return new StringReader(Encoding.Latin1.GetString(bytes));
        }
    }

    /// <summary>
    /// Index of the quote that closes the value name starting at position 0, honoring
    /// backslash escapes (<c>\"</c> and <c>\\</c>). Returns -1 when unterminated.
    /// </summary>
    private static int FindClosingQuote(ReadOnlySpan<char> line)
    {
        for (var i = 1; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '\\') { i++; continue; }
            if (c == '"') return i;
        }
        return -1;
    }
}
