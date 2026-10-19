using RegCompare.Models;
using RegCompare.Services;
using Xunit;

namespace RegCompare.Tests;

public class RegDataTests
{
    // The same data written two ways is the same data.
    [Theory]
    [InlineData("dword:0000000A", "dword:0000000a")]
    [InlineData("DWORD:0000000a", "dword:0000000a")]
    [InlineData("dword:1234", "dword:00001234")]
    [InlineData("hex(4):0a,00,00,00", "dword:0000000a")]
    [InlineData("hex:0A,0B", "hex:0a,0b")]
    [InlineData("hex(b):01, 00,00,00,00,00,00,00", "hex(b):01,00,00,00,00,00,00,00")]
    [InlineData("hex(b):01,00,00,00,00,00,00,00", "qword:0000000000000001")]
    [InlineData("hex:01,02", "hex(3):01,02")]
    [InlineData("hex:01,02,", "hex:01,02")]
    public void SameData_WrittenDifferently_IsTheSame(string a, string b) =>
        Assert.True(RegData.Same(a, false, b, false));

    [Theory]
    [InlineData("dword:0000000a", "dword:0000000b")]
    [InlineData("\"a\"", "\"A\"")]
    [InlineData("hex:01,02", "hex(0):01,02")]
    [InlineData("hex(2):41,00,00,00", "hex(7):41,00,00,00")]
    [InlineData("dword:zz", "dword:ZZ")]
    public void DifferentData_IsDifferent(string a, string b) =>
        Assert.False(RegData.Same(a, false, b, false));

    // A REGEDIT4 export writes text as ANSI bytes, a version 5 one as UTF-16: the same text.
    [Fact]
    public void Regedit4AndVersion5Text_IsTheSame()
    {
        Assert.True(RegData.Same("hex(2):25,50,41,54,48,25,00", true, "hex(2):25,00,50,00,41,00,54,00,48,00,25,00,00,00", false));
        Assert.True(RegData.Same("hex(7):61,00,62,00,00", true, "hex(7):61,00,00,00,62,00,00,00,00,00", false));
        Assert.False(RegData.Same("hex(2):25,50,41,54,48,25,00", true, "hex(2):25,00,50,00,00,00", false));
    }

    // Registry Editor's display: an empty string inside a multi-string stays, an expandable string ends
    // at its first NUL.
    [Fact]
    public void Display_MultiStringAndExpandString_AsRegistryEditorShowsThem()
    {
        Assert.Equal("A\n\nB", RegTypeFormatter.GetDisplayValue("hex(7):41,00,00,00,00,00,42,00,00,00,00,00"));
        Assert.Equal("A", RegTypeFormatter.GetDisplayValue("hex(2):41,00,00,00,42,00,00,00"));
    }

    // Text compares by its UTF-16 code units: unpaired surrogates are not all one character.
    [Fact]
    public void UnpairedSurrogates_AreDifferentText()
    {
        Assert.False(RegData.Same("hex(2):00,d8,00,00", false, "hex(2):01,d8,00,00", false));
        Assert.False(RegData.Same("hex(7):00,d8,00,00,00,00", false, "hex(7):fd,ff,00,00,00,00", false));
    }

    // reg.exe writes each line break of the data as \r\n: a file whose lines end with \n holds the same
    // string, as the grid shows it; a \r of the data stays.
    [Fact]
    public void StringLineBreaks_CompareAsShown()
    {
        Assert.True(RegData.Same("\"a\r\nb\"", false, "\"a\nb\"", false));
        Assert.False(RegData.Same("\"a\r\r\nb\"", false, "\"a\nb\"", false));
    }

    // A file whose lines were converted to \n (git, an editor) holds a string's data as it is; one
    // ending its lines with \r\n holds each \n of the data as \r\n (and a \r\n as \r\r\n).
    [Fact]
    public void StringLineBreaks_OfAConvertedFile_AreItsData()
    {
        Assert.True(RegData.Same("\"a\r\r\nb\"", false, "\"a\r\nb\"", false, aLfLines: false, bLfLines: true));
        Assert.True(RegData.Same("\"a\r\nb\"", false, "\"a\nb\"", false, aLfLines: false, bLfLines: true));
        Assert.False(RegData.Same("\"a\r\nb\"", false, "\"a\nb\"", false, aLfLines: true, bLfLines: true));
        Assert.Equal("a\r\nb", RegTypeFormatter.GetDisplayValue("\"a\r\r\nb\"", lfLines: false));
        Assert.Equal("a\r\nb", RegTypeFormatter.GetDisplayValue("\"a\r\nb\"", lfLines: true));
    }

    // What the grid shows the same compares the same: hex(04), a trailing comma.
    [Theory]
    [InlineData("hex(4):0a,00,00,00,")]
    [InlineData("hex(04):0a,00,00,00")]
    public void HexNumber_AnySpelling_IsShownAndComparedAsTheNumber(string raw)
    {
        Assert.Equal("0x0000000a (10)", RegTypeFormatter.GetDisplayValue(raw));
        Assert.Equal(RegValueType.RegDword, RegTypeFormatter.InferType(raw));
        Assert.True(RegData.Same(raw, false, "dword:0000000a", false));
    }
}

public class FileNamesTests
{
    // Files of the same name get as much of their folder as tells them apart.
    [Fact]
    public void SharedNames_GetTheirFolder()
    {
        Assert.Equal(["a.reg (x)", "a.reg (y)", "b.reg"], FileNames.Distinct([@"C:\x\a.reg", @"C:\y\a.reg", @"C:\y\b.reg"]));
        Assert.Equal([@"a.reg (C:\x)", @"a.reg (D:\x)", @"a.reg (C:\)"], FileNames.Distinct([@"C:\x\a.reg", @"D:\x\a.reg", @"C:\a.reg"]));
    }
}
