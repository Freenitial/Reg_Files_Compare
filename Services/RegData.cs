// Whether two raw .reg values hold the same data, however each file writes it.

using System;
using System.Globalization;
using System.Text;

namespace RegCompare.Services;

/// <summary>
/// Compares values by the data they hold rather than by their text: <c>dword:0000000A</c>,
/// <c>dword:a</c> and <c>hex(4):0a,00,00,00</c> are the same number; <c>hex:0A, 0B</c> and
/// <c>hex:0a,0b</c> the same bytes; a REGEDIT4 <c>hex(2):</c> (ANSI bytes) and a version 5 one
/// (UTF-16 bytes) the same text. Strings compare as written, except their line breaks: reg.exe writes
/// each \n of the data as \r\n (a \r\n as \r\r\n), which a file whose lines were converted to \n
/// (git, an editor) holds as the data itself (see <see cref="Models.RegFile.LfLines"/>). A value that
/// cannot be read compares by its text.
/// </summary>
public static class RegData
{
    /// <summary>True when <paramref name="a"/> (from a REGEDIT4 file when <paramref name="aRegedit4"/>, a
    /// file whose lines end with \n when <paramref name="aLfLines"/>) and <paramref name="b"/> hold the
    /// same data.</summary>
    public static bool Same(string a, bool aRegedit4, string b, bool bRegedit4, bool aLfLines = false, bool bLfLines = false)
    {
        if (string.Equals(a, b, StringComparison.Ordinal) && aRegedit4 == bRegedit4 && aLfLines == bLfLines) return true;
        return string.Equals(Canonical(a, aRegedit4, aLfLines), Canonical(b, bRegedit4, bLfLines), StringComparison.Ordinal);
    }

    /// <summary>One spelling for each data: what <see cref="Same"/> compares.</summary>
    public static string Canonical(string raw, bool regedit4, bool lfLines = false)
    {
        var span = raw.AsSpan();
        if (span.Length == 0 || raw == "-") return raw;
        // A string's data, as the values grid shows it.
        if (span[0] == '"') return StringData(raw, lfLines);

        if (span.StartsWith("dword:", StringComparison.OrdinalIgnoreCase))
        {
            return uint.TryParse(span[6..].Trim(), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var dword)
                ? "dword:" + dword.ToString("x8", CultureInfo.InvariantCulture)
                : raw;
        }
        if (span.StartsWith("qword:", StringComparison.OrdinalIgnoreCase))
        {
            return ulong.TryParse(span[6..].Trim(), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var qword)
                ? "qword:" + qword.ToString("x16", CultureInfo.InvariantCulture)
                : raw;
        }

        if (!TryParseHex(raw, out var type, out var bytes)) return raw;
        switch (type)
        {
            case 2 or 7:
                // Text: ANSI bytes in a REGEDIT4 file, UTF-16LE in a version 5 one, compared as UTF-16
                // bytes (decoding them would make every unpaired surrogate the same U+FFFD).
                if (!regedit4 && bytes.Length % 2 != 0) break;
                var utf16 = regedit4 ? Encoding.Unicode.GetBytes(AnsiEncoding.Current.GetString(bytes)) : bytes;
                return (type == 2 ? "expand:" : "multi:") + Convert.ToHexStringLower(utf16);
            case 4 when bytes.Length == 4:
                return "dword:" + BitConverter.ToUInt32(bytes).ToString("x8", CultureInfo.InvariantCulture);
            case 11 when bytes.Length == 8:
                return "qword:" + BitConverter.ToUInt64(bytes).ToString("x16", CultureInfo.InvariantCulture);
        }
        return "hex(" + type.ToString("x", CultureInfo.InvariantCulture) + "):" + Convert.ToHexStringLower(bytes);
    }

    /// <summary>
    /// A string's text as its data holds it: each \r\n of a file whose lines end with \r\n is one \n of
    /// the data; a file whose lines end with \n holds the data as it is.
    /// </summary>
    public static string StringData(string text, bool lfLines)
        => lfLines || !text.Contains('\r') ? text : text.Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>
    /// The type and bytes of hex data: <c>hex:</c> is REG_BINARY (type 3), <c>hex(n):</c> type n, in hex
    /// (<c>hex(4)</c>, <c>hex(04)</c>, <c>hex(b)</c>). False for other data, or a list that is not bytes.
    /// </summary>
    public static bool TryParseHex(string raw, out int type, out byte[] bytes)
    {
        bytes = [];
        if (!TryParseHexType(raw, out type, out var listStart)) return false;
        if (ParseBytes(raw.AsSpan(listStart)) is not { } parsed) return false;
        bytes = parsed;
        return true;
    }

    /// <summary>The type a <c>hex:</c> / <c>hex(n):</c> prefix names, and where the byte list starts.</summary>
    public static bool TryParseHexType(string raw, out int type, out int listStart)
    {
        var span = raw.AsSpan();
        if (span.StartsWith("hex:", StringComparison.OrdinalIgnoreCase))
        {
            type = 3;
            listStart = 4;
            return true;
        }
        if (span.StartsWith("hex(", StringComparison.OrdinalIgnoreCase)
            && span.IndexOf("):", StringComparison.Ordinal) is var close and > 4
            && int.TryParse(span[4..close], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out type))
        {
            listStart = close + 2;
            return true;
        }
        type = 0;
        listStart = 0;
        return false;
    }

    /// <summary>The bytes of a comma-separated hex list (blanks ignored), or null when one is not a byte.</summary>
    private static byte[]? ParseBytes(ReadOnlySpan<char> list)
    {
        var clean = new StringBuilder(list.Length);
        foreach (var c in list)
        {
            if (c is not (' ' or '\t' or '\r' or '\n')) clean.Append(c);
        }
        if (clean.Length == 0) return [];
        var parts = clean.ToString().Split(',');
        // A list ending with a comma (some tools write one): no byte after it.
        if (parts.Length > 1 && parts[^1].Length == 0) parts = parts[..^1];
        var bytes = new byte[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length is 0 or > 2
                || !byte.TryParse(parts[i], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out bytes[i]))
                return null;
        }
        return bytes;
    }
}
