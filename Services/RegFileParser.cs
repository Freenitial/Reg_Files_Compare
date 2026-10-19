// Parser that turns a .reg file into the nested dictionary used by the diff engine.
// Reads the file as Registry Editor does: the backslash line continuation of long hex values, strings
// whose data holds line breaks, comments after the data, lines that are neither a key nor a value.

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
    /// Parse the .reg file at <paramref name="path"/>. Each value keeps its data as written (its raw
    /// text, the type prefix included):
    /// <list type="bullet">
    /// <item>a string goes on until its closing quote, lines below when its data holds line breaks,
    /// which are kept exactly as the file writes them; what follows the closing quote (a comment) is
    /// not data. A quote on a later line closes it only when it ends that line (a comment may follow):
    /// otherwise the string is broken (an unescaped <c>\</c> before its quote) and its line is dropped,
    /// as is a string never closed;</item>
    /// <item>other data (dword:, hex…) ends at a <c>;</c> comment; a line ending with a backslash goes
    /// on with the next lines of hex data (blank and comment lines between them are skipped), joined;</item>
    /// <item><c>[key]</c> takes the text up to the <c>]</c> that ends the line or that a <c>;</c> comment
    /// follows (a name may hold <c>]</c>; without such a one, up to the last), without trailing backslashes; a key whose name starts with a space (or "- ") is not a key, and the values below it
    /// are dropped, as Registry Editor does;</item>
    /// <item>only lines starting with <c>"</c> or <c>@</c> are values: anything else is skipped.</item>
    /// </list>
    /// A key or value met twice keeps the later data. The parser never rejects a file.
    /// </summary>
    public RegFile Parse(string path)
    {
        var info = new FileInfo(path);
        var data = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? currentKey = null;
        bool? isRegedit4 = null;
        int lfEnds = 0, crlfEnds = 0;

        var text = Decode(File.ReadAllBytes(path));
        var pos = 0;
        while (NextLine(text, ref pos, out var lineStart, out var lineEnd))
        {
            // The line ending most lines use is the file's (a header added by a script may differ).
            CountEnding(text, lineEnd, 1, ref lfEnds, ref crlfEnds);
            var lead = lineStart;
            while (lead < lineEnd && IsBlank(text[lead])) lead++;
            var span = text.AsSpan(lead, lineEnd - lead).TrimEnd();
            if (span.IsEmpty) continue;

            // The first line is the header: "REGEDIT4" or "Windows Registry Editor Version 5.00".
            isRegedit4 ??= span.Equals(Regedit4Header, StringComparison.OrdinalIgnoreCase);
            if (span[0] == ';') continue;

            if (span[0] == '[')
            {
                currentKey = OpenKey(span, data);
                continue;
            }

            if (currentKey is null) continue;
            if (span[0] != '"' && span[0] != '@') continue;

            // The name: quoted (it may hold '=' and escaped quotes), or "@" for the default value, whose
            // name in the registry is empty (a value literally named "(Default)" is another value).
            string name;
            int afterName;
            if (span[0] == '"')
            {
                var close = IndexOfClosingQuote(span, 1);
                if (close < 0) continue;
                name = span[1..close].ToString();
                afterName = close + 1;
            }
            else
            {
                name = "";
                afterName = 1;
            }

            var eq = afterName;
            while (eq < span.Length && IsBlank(span[eq])) eq++;
            if (eq >= span.Length || span[eq] != '=') continue;
            var valueStart = eq + 1;
            while (valueStart < span.Length && IsBlank(span[valueStart])) valueStart++;
            if (valueStart >= span.Length) continue;

            if (span[valueStart] == '"')
            {
                // A string, up to its closing quote: on this line, or lines below when its data holds
                // line breaks (Registry Editor writes them as they are). Kept exactly as written.
                var start = lead + valueStart;
                var close = IndexOfClosingQuote(text.AsSpan(), start + 1);
                // Never closed, or closed by a quote that does not end its line (the next value's): a
                // broken string, whose line alone is dropped.
                if (close < 0 || (close >= lineEnd && !EndsItsLine(text, close + 1))) continue;
                currentKey[name] = text[start..(close + 1)];
                // A string that went on below: the lines it took are done. The end of its first line
                // was data; its line ends after the closing quote.
                if (close >= lineEnd)
                {
                    CountEnding(text, lineEnd, -1, ref lfEnds, ref crlfEnds);
                    var after = text.AsSpan(close).IndexOfAny('\r', '\n');
                    if (after >= 0) CountEnding(text, close + after, 1, ref lfEnds, ref crlfEnds);
                    pos = EndOfLineAfter(text, close);
                }
                continue;
            }

            var value = CutComment(span[valueStart..]).TrimEnd();
            if (value.Length > 0 && value[^1] == '\\')
            {
                currentKey[name] = ReadContinuation(text, ref pos, value[..^1].TrimEnd());
                continue;
            }
            currentKey[name] = value.ToString();
        }

        return new RegFile
        {
            Path = path,
            FileName = System.IO.Path.GetFileName(path),
            Modified = info.LastWriteTime,
            SizeBytes = info.Length,
            Data = data,
            IsRegedit4 = isRegedit4 == true,
            LfLines = lfEnds > crlfEnds,
        };
    }

    /// <summary>
    /// The key a <c>[…]</c> line opens (created on first sight), or null when the line names no key:
    /// no closing bracket, a name starting with a space or with "- " (Registry Editor ignores those
    /// lines and the values below them).
    /// </summary>
    private static Dictionary<string, string>? OpenKey(ReadOnlySpan<char> line, Dictionary<string, Dictionary<string, string>> data)
    {
        var close = KeyEnd(line);
        if (close < 1) return null;
        var name = line[1..close];
        if (name.IsEmpty || IsBlank(name[0])) return null;
        if (name[0] == '-' && (name.Length == 1 || IsBlank(name[1]))) return null;
        // "[HKEY_CURRENT_USER\Software\]" is "[HKEY_CURRENT_USER\Software]".
        name = name.TrimEnd('\\');
        if (name.IsEmpty || name.SequenceEqual("-")) return null;

        var keyPath = name.ToString();
        if (!data.TryGetValue(keyPath, out var key))
        {
            key = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            data[keyPath] = key;
        }
        return key;
    }

    /// <summary>
    /// The <c>]</c> closing a key line: the first one that ends the line or that a <c>;</c> comment
    /// follows (a name may hold <c>]</c> itself: <c>[HKEY_CURRENT_USER\Software\[x]]</c>); the last one
    /// when none does.
    /// </summary>
    private static int KeyEnd(ReadOnlySpan<char> line)
    {
        var i = line.IndexOf(']');
        while (i >= 0)
        {
            var rest = line[(i + 1)..].TrimStart(" \t");
            if (rest.IsEmpty || rest[0] == ';') return i;
            var next = line[(i + 1)..].IndexOf(']');
            if (next < 0) break;
            i += next + 1;
        }
        return line.LastIndexOf(']');
    }

    /// <summary>Count (or, with a <paramref name="delta"/> of -1, take back) the line ending at <paramref name="end"/>.</summary>
    private static void CountEnding(string text, int end, int delta, ref int lf, ref int crlf)
    {
        if (end >= text.Length) return;
        if (text[end] == '\n') lf += delta;
        else if (end + 1 < text.Length && text[end + 1] == '\n') crlf += delta;
    }

    /// <summary>True when only blanks, or a <c>;</c> comment, follow <paramref name="index"/> on its line.</summary>
    private static bool EndsItsLine(string text, int index)
    {
        while (index < text.Length && IsBlank(text[index])) index++;
        return index >= text.Length || text[index] is '\r' or '\n' or ';';
    }

    /// <summary>
    /// The rest of a value continued with a backslash: the next lines of hex data, joined (a line
    /// ending with a backslash goes on again). Blank and comment lines between them are skipped; a line
    /// that is not hex data ends the value and is read again as a line of its own.
    /// </summary>
    private static string ReadContinuation(string text, ref int pos, ReadOnlySpan<char> head)
    {
        var value = new StringBuilder();
        value.Append(head);
        while (true)
        {
            var before = pos;
            if (!NextLine(text, ref pos, out var lineStart, out var lineEnd)) break;
            var line = text.AsSpan(lineStart, lineEnd - lineStart).Trim();
            if (line.IsEmpty || line[0] == ';') continue;

            var piece = CutComment(line).TrimEnd();
            if (!IsHexData(piece))
            {
                pos = before;
                break;
            }
            var goesOn = piece.Length > 0 && piece[^1] == '\\';
            if (goesOn) piece = piece[..^1].TrimEnd();
            value.Append(piece);
            if (!goesOn) break;
        }
        return value.ToString();
    }

    /// <summary>Hex digits, commas and blanks (and the continuation backslash): a line of hex data.</summary>
    private static bool IsHexData(ReadOnlySpan<char> line)
    {
        if (line.IsEmpty) return false;
        foreach (var c in line)
        {
            if (!(char.IsAsciiHexDigit(c) || c is ',' or ' ' or '\t' or '\\')) return false;
        }
        return true;
    }

    /// <summary>The data before a <c>;</c> comment (dword and hex data never hold one).</summary>
    private static ReadOnlySpan<char> CutComment(ReadOnlySpan<char> data)
    {
        var semicolon = data.IndexOf(';');
        return semicolon < 0 ? data : data[..semicolon];
    }

    private static bool IsBlank(char c) => c is ' ' or '\t';

    /// <summary>
    /// The next line of <paramref name="text"/> from <paramref name="pos"/>: its bounds (without its
    /// end, which is \r\n, \n or \r), and <paramref name="pos"/> moved past it. False at the end.
    /// </summary>
    private static bool NextLine(string text, ref int pos, out int start, out int end)
    {
        start = pos;
        end = pos;
        if (pos >= text.Length) return false;
        var next = text.AsSpan(pos).IndexOfAny('\r', '\n');
        if (next < 0)
        {
            end = text.Length;
            pos = text.Length;
            return true;
        }
        end = pos + next;
        pos = end + (text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? 2 : 1);
        return true;
    }

    /// <summary>The position after the end of the line holding <paramref name="index"/>.</summary>
    private static int EndOfLineAfter(string text, int index)
    {
        var next = text.AsSpan(index).IndexOfAny('\r', '\n');
        if (next < 0) return text.Length;
        var end = index + next;
        return end + (text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? 2 : 1);
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
