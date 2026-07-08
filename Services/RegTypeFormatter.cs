// Type inference and display formatting for raw .reg value strings.
// Renders DWORD/QWORD with their decimal value, and decodes MULTI_SZ / EXPAND_SZ
// hex bytes back to UTF-16LE text.

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
        if (span.StartsWith("hex(2):", StringComparison.OrdinalIgnoreCase)) return RegValueType.RegExpandSz;
        if (span.StartsWith("hex(7):", StringComparison.OrdinalIgnoreCase)) return RegValueType.RegMultiSz;
        if (span.StartsWith("hex(b):", StringComparison.OrdinalIgnoreCase)) return RegValueType.RegQword;
        if (span.StartsWith("hex(4):", StringComparison.OrdinalIgnoreCase)) return RegValueType.RegDword;
        // Any other hex(N) form (REG_NONE, REG_LINK, resource lists...) is shown as binary data.
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
    /// <item>MULTI_SZ: decodes UTF-16LE hex bytes, splits on null chars, joins with newlines.</item>
    /// <item>EXPAND_SZ: decodes UTF-16LE hex bytes, trims trailing nulls.</item>
    /// <item>BINARY and unrecognized values: returned as-is.</item>
    /// </list>
    /// </summary>
    public static string GetDisplayValue(string raw)
    {
        var span = raw.AsSpan();
        if (span.Length >= 2 && span[0] == '"' && span[^1] == '"')
        {
            return UnescapeRegString(raw.AsSpan(1, raw.Length - 2));
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
        if (span.StartsWith("hex(7):", StringComparison.OrdinalIgnoreCase))
        {
            return DecodeMultiSz(raw.AsSpan(7)) ?? raw;
        }
        if (span.StartsWith("hex(2):", StringComparison.OrdinalIgnoreCase))
        {
            return DecodeExpandSz(raw.AsSpan(7)) ?? raw;
        }
        if (span.StartsWith("hex(b):", StringComparison.OrdinalIgnoreCase))
        {
            return DecodeLittleEndianNumber(raw.AsSpan(7), byteCount: 8) ?? raw;
        }
        if (span.StartsWith("hex(4):", StringComparison.OrdinalIgnoreCase))
        {
            return DecodeLittleEndianNumber(raw.AsSpan(7), byteCount: 4) ?? raw;
        }
        return raw;
    }

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

    private static string? DecodeMultiSz(ReadOnlySpan<char> hexBytes)
    {
        var bytes = ParseHexBytes(hexBytes);
        if (bytes is null) return null;
        var text = Encoding.Unicode.GetString(bytes);
        var lines = text.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        return string.Join('\n', lines);
    }

    private static string? DecodeExpandSz(ReadOnlySpan<char> hexBytes)
    {
        var bytes = ParseHexBytes(hexBytes);
        if (bytes is null) return null;
        return Encoding.Unicode.GetString(bytes).TrimEnd('\0');
    }

    /// <summary>
    /// Decode a little-endian hex byte list (the <c>hex(b):</c> QWORD / <c>hex(4):</c> DWORD
    /// serializations) into the same <c>0xHHHH (decimal)</c> display used for dword/qword values.
    /// Returns null when the byte count does not match the expected width.
    /// </summary>
    private static string? DecodeLittleEndianNumber(ReadOnlySpan<char> hexBytes, int byteCount)
    {
        var bytes = ParseHexBytes(hexBytes);
        if (bytes is null || bytes.Length != byteCount) return null;
        var value = 0UL;
        for (var i = bytes.Length - 1; i >= 0; i--) value = (value << 8) | bytes[i];
        var hex = value.ToString(byteCount == 8 ? "x16" : "x8", CultureInfo.InvariantCulture);
        return $"0x{hex} ({value})";
    }

    /// <summary>
    /// Parse a comma-separated list of hex bytes (e.g. <c>"41,00,42,00"</c>) into a byte array.
    /// Returns null if any token is not a valid two-digit hex number.
    /// </summary>
    private static byte[]? ParseHexBytes(ReadOnlySpan<char> hexBytes)
    {
        // Quick whitespace strip via a small builder. The .reg format uses commas as separators
        // and may contain spaces (after line-continuation joins by the parser).
        var clean = new StringBuilder(hexBytes.Length);
        foreach (var c in hexBytes)
        {
            if (c is ' ' or '\t' or '\r' or '\n') continue;
            clean.Append(c);
        }
        if (clean.Length == 0) return [];

        var parts = clean.ToString().Split(',');
        var bytes = new byte[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!byte.TryParse(parts[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
            {
                return null;
            }
        }
        return bytes;
    }
}
