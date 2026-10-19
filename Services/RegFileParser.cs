// Parser that turns a .reg file into the nested dictionary used by the diff engine.
// Handles the .reg backslash line-continuation used by long hex values, and strings whose data
// holds line breaks.

using System;
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
    private const string Regedit4Header = "REGEDIT4";
    private const string Version5Header = "Windows Registry Editor";

    /// <summary>
    /// Parse the .reg file at <paramref name="path"/>. Lines ending with a backslash (the .reg
    /// continuation marker of long hex values) are joined with the next line, so a value that wraps
    /// still produces a single raw value string. A quoted string goes on until its closing quote,
    /// lines below when its data holds line breaks (Registry Editor writes them as they are).
    /// </summary>
    public RegFile Parse(string path)
    {
        var info = new FileInfo(path);
        var data = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? currentKey = null;
        bool? isRegedit4 = null;

        string? pendingValueName = null;
        var pendingValueBuilder = new StringBuilder();

        using var reader = new StringReader(Decode(File.ReadAllBytes(path)));
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
            // The first line is the header: "REGEDIT4" or "Windows Registry Editor Version 5.00".
            isRegedit4 ??= span.Equals(Regedit4Header, StringComparison.OrdinalIgnoreCase);
            if (span[0] == ';') continue;

            if (span[0] == '[' && span[^1] == ']')
            {
                var keyPath = span[1..^1].ToString();
                if (!data.TryGetValue(keyPath, out currentKey))
                {
                    currentKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
                var close = IndexOfClosingQuote(span, 1);
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
                // "@" is the default value, whose name in the registry is empty. A value literally
                // named "(Default)" is another value.
                name = rawName.Length == 1 && rawName[0] == '@'
                    ? ""
                    : rawName.Trim('"').ToString();
                rawValue = span[(eqIndex + 1)..].Trim();
            }

            // A string whose closing quote is not on this line: its data holds line breaks, written
            // as they are. The value goes on, line by line, until the closing quote.
            if (rawValue.Length > 0 && rawValue[0] == '"' && IndexOfClosingQuote(rawValue, 1) < 0)
            {
                var text = new StringBuilder();
                // The first line keeps its trailing blanks: they are data, before the line break.
                text.Append(rawValue).Append(line.AsSpan(line.AsSpan().TrimEnd().Length));
                string? next;
                while ((next = reader.ReadLine()) != null)
                {
                    text.Append("\r\n");
                    var close = IndexOfClosingQuote(next, 0);
                    if (close >= 0)
                    {
                        text.Append(next.AsSpan(0, close + 1));
                        break;
                    }
                    text.Append(next);
                }
                currentKey[name] = text.ToString();
                continue;
            }

            // Long hex values can span multiple physical lines when their last character is a
            // backslash. Open a continuation buffer and accumulate.
            if (rawValue.Length > 0 && rawValue[0] != '"' && rawValue[^1] == '\\')
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
            IsRegedit4 = isRegedit4 == true,
        };
    }

    /// <summary>
    /// True when the file's first line is a .reg header (<c>Windows Registry Editor …</c> or
    /// <c>REGEDIT4</c>), in any encoding the parser reads. Used for files without extension.
    /// </summary>
    public static bool HasRegHeader(string path)
    {
        var buffer = new byte[4096];
        int read;
        using (var stream = File.OpenRead(path))
            read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);

        var text = Decode(buffer.AsSpan(0, read).ToArray(), partial: true).AsSpan().TrimStart();
        return text.StartsWith(Version5Header, StringComparison.OrdinalIgnoreCase)
            || text.StartsWith(Regedit4Header, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Decode the file's bytes: byte-order mark when present; otherwise NUL bytes near the start
    /// indicate UTF-16LE (regedit v5 exports without BOM); otherwise a <c>REGEDIT4</c> file is ANSI
    /// (the system code page, as Registry Editor writes it), and anything else strict UTF-8 with
    /// the ANSI code page as fallback. <paramref name="partial"/>: the bytes are only the start of
    /// the file (a multi-byte character may be cut at the end).
    /// </summary>
    private static string Decode(byte[] bytes, bool partial = false)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        var probe = Math.Min(bytes.Length, 512);
        for (var i = 0; i < probe; i++)
        {
            if (bytes[i] == 0) return Encoding.Unicode.GetString(bytes);
        }

        if (StartsWithAscii(bytes, Regedit4Header)) return AnsiEncoding.Current.GetString(bytes);

        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: !partial).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return AnsiEncoding.Current.GetString(bytes);
        }
    }

    /// <summary>True when the bytes start (after blank lines and spaces) with the ASCII text <paramref name="prefix"/>, in any case.</summary>
    private static bool StartsWithAscii(byte[] bytes, string prefix)
    {
        var i = 0;
        while (i < bytes.Length && bytes[i] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') i++;
        if (bytes.Length - i < prefix.Length) return false;
        for (var j = 0; j < prefix.Length; j++)
        {
            if (char.ToUpperInvariant((char)bytes[i + j]) != char.ToUpperInvariant(prefix[j])) return false;
        }
        return true;
    }

    /// <summary>
    /// Index of the quote that closes a quoted text, searched from <paramref name="start"/> and
    /// honoring backslash escapes (<c>\"</c> and <c>\\</c>). Returns -1 when there is none.
    /// </summary>
    private static int IndexOfClosingQuote(ReadOnlySpan<char> text, int start)
    {
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\\') { i++; continue; }
            if (c == '"') return i;
        }
        return -1;
    }
}
