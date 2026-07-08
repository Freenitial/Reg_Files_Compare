using RegCompare.Models;
using RegCompare.Services;
using Xunit;

namespace RegCompare.Tests;

public class RegTypeFormatterTests
{
    [Theory]
    [InlineData("\"hello\"", RegValueType.RegSz)]
    [InlineData("dword:0000ffff", RegValueType.RegDword)]
    [InlineData("qword:0000000000000010", RegValueType.RegQword)]
    [InlineData("hex:01,02,03", RegValueType.RegBinary)]
    [InlineData("hex(2):25,00,50", RegValueType.RegExpandSz)]
    [InlineData("hex(7):41,00,42", RegValueType.RegMultiSz)]
    [InlineData("hex(b):10,00,00,00,00,00,00,00", RegValueType.RegQword)]
    [InlineData("hex(4):ff,ff,00,00", RegValueType.RegDword)]
    [InlineData("hex(0):", RegValueType.RegBinary)]
    [InlineData("foo", RegValueType.RegSz)]
    public void InferType_RecognizesPrefixes(string raw, RegValueType expected)
    {
        Assert.Equal(expected, RegTypeFormatter.InferType(raw));
    }

    [Theory]
    [InlineData(RegValueType.RegSz, "SZ")]
    [InlineData(RegValueType.RegExpandSz, "EXPAND_SZ")]
    [InlineData(RegValueType.RegMultiSz, "MULTI_SZ")]
    [InlineData(RegValueType.RegDword, "DWORD")]
    [InlineData(RegValueType.RegQword, "QWORD")]
    [InlineData(RegValueType.RegBinary, "BINARY")]
    public void GetTypeName_StripsRegPrefix(RegValueType type, string expected)
    {
        Assert.Equal(expected, RegTypeFormatter.GetTypeName(type));
    }

    [Theory]
    [InlineData("\"hello\"", "hello")]
    [InlineData("\"C:\\\\Foo\\\\Bar\"", "C:\\Foo\\Bar")]
    [InlineData("dword:0000ffff", "0x0000ffff (65535)")]
    [InlineData("dword:00000001", "0x00000001 (1)")]
    [InlineData("qword:0000000000000010", "0x0000000000000010 (16)")]
    [InlineData("hex(b):10,00,00,00,00,00,00,00", "0x0000000000000010 (16)")]
    [InlineData("hex(4):ff,ff,00,00", "0x0000ffff (65535)")]
    [InlineData("hex:01,02,03", "hex:01,02,03")]
    public void GetDisplayValue_StripsPrefixes(string raw, string expected)
    {
        Assert.Equal(expected, RegTypeFormatter.GetDisplayValue(raw));
    }

    [Fact]
    public void GetDisplayValue_DecodesMultiSzToNewlineJoinedStrings()
    {
        // hex(7):41,00,42,00,00,00,43,00,44,00,00,00,00,00 = "AB" + "CD" (UTF-16LE, double-null terminated)
        var raw = "hex(7):41,00,42,00,00,00,43,00,44,00,00,00,00,00";
        Assert.Equal("AB\nCD", RegTypeFormatter.GetDisplayValue(raw));
    }

    [Fact]
    public void GetDisplayValue_DecodesExpandSz()
    {
        // hex(2):25,00,50,00,41,00,54,00,48,00,25,00,00,00 = "%PATH%" (UTF-16LE, null-terminated)
        var raw = "hex(2):25,00,50,00,41,00,54,00,48,00,25,00,00,00";
        Assert.Equal("%PATH%", RegTypeFormatter.GetDisplayValue(raw));
    }
}
