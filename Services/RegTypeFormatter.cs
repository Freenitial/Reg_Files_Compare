// Type inference and display formatting for raw .reg value strings.
// Renders DWORD/QWORD with their decimal value, and decodes MULTI_SZ / EXPAND_SZ
// hex bytes back to text (UTF-16LE, or ANSI in a REGEDIT4 file).

using System;
using System.Globalization;
using System.Text;
using RegCompare.Models;

namespace RegCompare.Services;

/// <summary>
/// Static helpers that infer the registry value type from the raw .reg string and produce
/// the display representation.
/// </summary>
public static class RegTypeFormatter
{
    /// <summary>Infer the registry value type from the raw value string.</summary>
    public static RegValueType InferType(string raw)
    {
        var span = raw.AsSpan();
        if (span.Length >= 2 && span[0] == '"' && span[^1] == '"') return RegValueType.RegSz;
        // The type a hex(n) prefix names, however written (hex(4), hex(04)); any other type (REG_NONE,
        // REG_LINK, resource lists...) is shown as binary data.
        if (RegData.TryParseHexType(raw, out var type, out _))
        {
            return type switch
            {
                2 => RegValueType.RegExpandSz,
                7 => RegValueType.RegMultiSz,
                0xb => RegValueType.RegQword,
                4 => RegValueType.RegDword,
                _ => RegValueType.RegBinary,
            };
        }
        if (span.StartsWith("hex(", StringComparison.OrdinalIgnoreCase)) return RegValueType.RegBinary;
        if (span.StartsWith("hex:", StringComparison.OrdinalIgnoreCase)) return RegValueType.RegBinary;
        if (span.StartsWith("dword:", StringComparison.OrdinalIgnoreCase)) return RegValueType.RegDword;
        if (span.StartsWith("qword:", StringComparison.OrdinalIgnoreCase)) return RegValueType.RegQword;
        return RegValueType.RegSz;
    }

    /// <summary>
    /// Returns the short type name used in the values grid (the <c>REG_</c> prefix is dropped
    /// to keep the Type column narrow - it is implicit in the column header).
    /// </summary>
    public static string GetTypeName(RegValueType type) => type switch
    {
        RegValueType.RegSz => "SZ",
        RegValueType.RegExpandSz => "EXPAND_SZ",
        RegValueType.RegMultiSz => "MULTI_SZ",
        RegValueType.RegDword => "DWORD",
        RegValueType.RegQword => "QWORD",
        RegValueType.RegBinary => "BINARY",
        _ => "SZ",
    };

    /// <summary>
    /// Strip the type prefix and quoting from the raw .reg value to produce the human-readable display string.
    /// <list type="bullet">
    /// <item>SZ: returns inner text with .reg escapes (<c>\\</c> -&gt; <c>\</c>, <c>\"</c> -&gt; <c>"</c>) decoded.</item>
    /// <item>DWORD/QWORD: returns <c>0xHHHH (decimal)</c>.</item>
    /// <item>MULTI_SZ: decodes the hex bytes, splits on null chars, joins with newlines.</item>
    /// <item>EXPAND_SZ: decodes the hex bytes, trims trailing nulls.</item>
    /// <item>BINARY and unrecognized values: returned as-is.</item>
    /// </list>
    /// MULTI_SZ and EXPAND_SZ bytes are UTF-16LE in a version 5 file (an odd byte count is shown as
    /// raw text), ANSI in a <c>REGEDIT4</c> file (<paramref name="regedit4"/>).
    /// </summary>
    public static string GetDisplayValue(string raw, bool regedit4 = false, bool lfLines = false)
    {
        var span = raw.AsSpan();
        if (span.Length >= 2 && span[0] == '"' && span[^1] == '"')
        {
            // reg.exe writes each \n of the data as \r\n (a \r\n as \r\r\n): the data is shown, one break
            // per break of the data (see RegData.StringData).
            return RegData.StringData(UnescapeRegString(raw.AsSpan(1, raw.Length - 2)), lfLines);
        }
        if (span.StartsWith("dword:", StringComparison.OrdinalIgnoreCase))
        {
            var hex = raw[6..];
            if (uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var dec))
            {
                return $"0x{hex} ({dec})";
            }
            return "0x" + hex;
        }
        if (span.StartsWith("qword:", StringComparison.OrdinalIgnoreCase))
        {
            var hex = raw[6..];
            if (ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var dec))
            {
                return $"0x{hex} ({dec})";
            }
            return "0x" + hex;
        }
        // Hex data read as RegData compares it (hex(04), a trailing comma), so that what shows the same
        // compares the same.
        if (RegData.TryParseHex(raw, out var type, out var bytes))
        {
            return type switch
            {
                7 => DecodeMultiSz(bytes, regedit4),
                2 => DecodeExpandSz(bytes, regedit4),
                0xb => DecodeLittleEndianNumber(bytes, byteCount: 8),
                4 => DecodeLittleEndianNumber(bytes, byteCount: 4),
                _ => null,
            } ?? raw;
        }
        return raw;
    }

    /// <summary>A text as written between quotes in a .reg file (a value name), without its escapes:
    /// <c>\\</c> -&gt; <c>\</c>, <c>\"</c> -&gt; <c>"</c>.</summary>
    public static string Unescape(string text) => UnescapeRegString(text);

    private static string UnescapeRegString(ReadOnlySpan<char> inner)
    {
        if (inner.IndexOf('\\') < 0) return inner.ToString();

        var sb = new StringBuilder(inner.Length);
        for (var i = 0; i < inner.Length; i++)
        {
            var c = inner[i];
            if (c == '\\' && i + 1 < inner.Length)
            {
                var next = inner[i + 1];
                if (next == '\\' || next == '"')
                {
                    sb.Append(next);
                    i++;
                    continue;
                }
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static string? DecodeMultiSz(byte[] bytes, bool ansi)
    {
        var text = DecodeText(bytes, ansi);
        if (text is null) return null;
        // Each string ends with a NUL, the list with one more: an empty string in the middle stays.
        return string.Join('\n', text.TrimEnd('\0').Split('\0'));
    }

    /// <summary>The text up to its first NUL, as Registry Editor shows it.</summary>
    private static string? DecodeExpandSz(byte[] bytes, bool ansi)
    {
        var text = DecodeText(bytes, ansi);
        if (text is null) return null;
        var nul = text.IndexOf('\0');
        return nul < 0 ? text : text[..nul];
    }

    /// <summary>
    /// The text held by bytes: ANSI, or UTF-16LE. Null for an odd number of bytes in UTF-16 (not text, then).
    /// </summary>
    private static string? DecodeText(byte[] bytes, bool ansi)
    {
        if (ansi) return AnsiEncoding.Current.GetString(bytes);
        if (bytes.Length % 2 != 0) return null;
        return Encoding.Unicode.GetString(bytes);
    }

    /// <summary>
    /// Decode a little-endian hex byte list (the <c>hex(b):</c> QWORD / <c>hex(4):</c> DWORD
    /// serializations) into the same <c>0xHHHH (decimal)</c> display used for dword/qword values.
    /// Returns null when the byte count does not match the expected width.
    /// </summary>
    private static string? DecodeLittleEndianNumber(byte[] bytes, int byteCount)
    {
        if (bytes.Length != byteCount) return null;
        var value = 0UL;
        for (var i = bytes.Length - 1; i >= 0; i--) value = (value << 8) | bytes[i];
        var hex = value.ToString(byteCount == 8 ? "x16" : "x8", CultureInfo.InvariantCulture);
        return $"0x{hex} ({value})";
    }
}
