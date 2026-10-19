using System.IO;
using System.Text;
using RegCompare.Services;
using Xunit;

namespace RegCompare.Tests;

public class RegFileParserTests
{
    private static string WriteTemp(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".reg");
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    [Fact]
    public void Parse_BasicFile_KeysAndValues()
    {
        var path = WriteTemp("""
Windows Registry Editor Version 5.00

[HKEY_LOCAL_MACHINE\Software\RegCompareTest]
"Foo"="Bar"
@="Default value here"
"Counter"=dword:000000ff
"Big"=qword:0000000000000010
"Bytes"=hex:01,02,03
"Multi"=hex(7):41,00,42,00,00,00,00,00
"Expand"=hex(2):25,00,50,00,41,00,54,00,48,00,25,00,00,00
""");

        try
        {
            var parser = new RegFileParser();
            var file = parser.Parse(path);

            Assert.Single(file.Data);
            Assert.True(file.Data.ContainsKey("HKEY_LOCAL_MACHINE\\Software\\RegCompareTest"));

            var values = file.Data["HKEY_LOCAL_MACHINE\\Software\\RegCompareTest"];
            Assert.Equal(7, values.Count);
            Assert.Equal("\"Bar\"", values["Foo"]);
            Assert.Equal("\"Default value here\"", values[""]);
            Assert.Equal("dword:000000ff", values["Counter"]);
            Assert.Equal("qword:0000000000000010", values["Big"]);
            Assert.Equal("hex:01,02,03", values["Bytes"]);
            Assert.Equal("hex(7):41,00,42,00,00,00,00,00", values["Multi"]);
            Assert.Equal("hex(2):25,00,50,00,41,00,54,00,48,00,25,00,00,00", values["Expand"]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Parse_MultipleKeys_AreIndependent()
    {
        var path = WriteTemp("""
[HKEY_CURRENT_USER\A]
"v1"="x"

[HKEY_CURRENT_USER\B]
"v2"="y"
""");
        try
        {
            var parser = new RegFileParser();
            var file = parser.Parse(path);

            Assert.Equal(2, file.Data.Count);
            Assert.Equal("\"x\"", file.Data["HKEY_CURRENT_USER\\A"]["v1"]);
            Assert.Equal("\"y\"", file.Data["HKEY_CURRENT_USER\\B"]["v2"]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Parse_CommentLineWithEquals_InsideKey_IsIgnored()
    {
        var path = WriteTemp("""
[HKEY_CURRENT_USER\Test]
; note = this is a comment, not a value
"a"="b"
""");
        try
        {
            var file = new RegFileParser().Parse(path);
            var values = file.Data["HKEY_CURRENT_USER\\Test"];
            Assert.Single(values);
            Assert.Equal("\"b\"", values["a"]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Parse_ValueNameContainingEqualsAndEscapedQuote_IsSplitAtSeparator()
    {
        var path = WriteTemp("""
[HKEY_CURRENT_USER\Test]
"a=b"="v1"
"say \"hi\""="v2"
""");
        try
        {
            var file = new RegFileParser().Parse(path);
            var values = file.Data["HKEY_CURRENT_USER\\Test"];
            Assert.Equal("\"v1\"", values["a=b"]);
            Assert.Equal("\"v2\"", values["say \\\"hi\\\""]);
        }
        finally { File.Delete(path); }
    }

    // A REGEDIT4 file is ANSI: read in the system code page (Windows-1252 has € and ’ where Latin-1
    // has control characters), and flagged so its hex(2)/hex(7) data are read as ANSI too.
    [Fact]
    public void Parse_Regedit4File_IsReadInTheAnsiCodePage()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".reg");
        const string text = "café 5 € l’été";
        var content = $"REGEDIT4\r\n\r\n[HKEY_CURRENT_USER\\Test]\r\n\"accent\"=\"{text}\"\r\n";
        var ansi = AnsiEncoding.Current;
        File.WriteAllBytes(path, ansi.GetBytes(content));
        try
        {
            var file = new RegFileParser().Parse(path);
            Assert.True(file.IsRegedit4);
            Assert.Equal($"\"{ansi.GetString(ansi.GetBytes(text))}\"", file.Data["HKEY_CURRENT_USER\\Test"]["accent"]);
        }
        finally { File.Delete(path); }
    }

    // A version 5 file saved as ANSI by a text editor (no BOM, not valid UTF-8) falls back to ANSI.
    [Fact]
    public void Parse_Version5FileInAnsi_FallsBackToTheAnsiCodePage()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".reg");
        var content = "Windows Registry Editor Version 5.00\r\n\r\n[HKEY_CURRENT_USER\\Test]\r\n\"accent\"=\"café\"\r\n";
        File.WriteAllBytes(path, AnsiEncoding.Current.GetBytes(content));
        try
        {
            var file = new RegFileParser().Parse(path);
            Assert.False(file.IsRegedit4);
            Assert.Equal("\"café\"", file.Data["HKEY_CURRENT_USER\\Test"]["accent"]);
        }
        finally { File.Delete(path); }
    }

    // "@" is the default value, stored under the registry's own name for it (empty); a value
    // literally named "(Default)" is another value.
    [Fact]
    public void Parse_DefaultValue_IsNotTheValueNamedDefault()
    {
        var path = WriteTemp("""
Windows Registry Editor Version 5.00

[HKEY_CURRENT_USER\Test]
@="the default"
"(Default)"="a value named (Default)"
""");
        try
        {
            var values = new RegFileParser().Parse(path).Data["HKEY_CURRENT_USER\\Test"];
            Assert.Equal(2, values.Count);
            Assert.Equal("\"the default\"", values[""]);
            Assert.Equal("\"a value named (Default)\"", values["(Default)"]);
        }
        finally { File.Delete(path); }
    }

    // Registry Editor writes a string's line breaks as they are: the value goes on until its closing
    // quote, whatever the lines in between look like.
    [Fact]
    public void Parse_StringWithLineBreaks_IsOneValue()
    {
        var path = WriteTemp("Windows Registry Editor Version 5.00\r\n\r\n[HKEY_CURRENT_USER\\Test]\r\n"
            + "\"Tip\"=\"Backup \r\nLast run: FAILED = disk full\r\n[not a key]\r\n  said \\\"no\\\"\"\r\n"
            + "\"Next\"=\"x\"\r\n");
        try
        {
            var file = new RegFileParser().Parse(path);
            Assert.Single(file.Data);
            var values = file.Data["HKEY_CURRENT_USER\\Test"];
            Assert.Equal(2, values.Count);
            Assert.Equal("\"Backup \r\nLast run: FAILED = disk full\r\n[not a key]\r\n  said \\\"no\\\"\"", values["Tip"]);
            Assert.Equal("\"x\"", values["Next"]);
            // Shown with one line break per break of the data.
            Assert.Equal("Backup \nLast run: FAILED = disk full\n[not a key]\n  said \"no\"", RegTypeFormatter.GetDisplayValue(values["Tip"]));
        }
        finally { File.Delete(path); }
    }

    // A long hex value wraps with a backslash and an indented next line: joined into one value.
    [Fact]
    public void Parse_HexContinuationLines_AreJoined()
    {
        var path = WriteTemp("Windows Registry Editor Version 5.00\r\n\r\n[HKEY_CURRENT_USER\\Test]\r\n"
            + "\"Bin\"=hex:01,02,\\\r\n  03,04,\\\r\n  05\r\n\"After\"=\"y\"\r\n");
        try
        {
            var values = new RegFileParser().Parse(path).Data["HKEY_CURRENT_USER\\Test"];
            Assert.Equal("hex:01,02,03,04,05", values["Bin"]);
            Assert.Equal("\"y\"", values["After"]);
        }
        finally { File.Delete(path); }
    }

    // A file without extension is taken when its first line is a .reg header, in UTF-16 or ANSI.
    [Fact]
    public void HasRegHeader_RecognizesBothHeaders()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var v5 = Path.Combine(dir, "v5");
            File.WriteAllText(v5, "Windows Registry Editor Version 5.00\r\n", Encoding.Unicode);
            var v4 = Path.Combine(dir, "v4");
            File.WriteAllBytes(v4, Encoding.ASCII.GetBytes("REGEDIT4\r\n"));
            var other = Path.Combine(dir, "other");
            File.WriteAllText(other, "Just some text\r\n");

            Assert.True(RegFileParser.HasRegHeader(v5));
            Assert.True(RegFileParser.HasRegHeader(v4));
            Assert.False(RegFileParser.HasRegHeader(other));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Parse_Utf16WithoutBom_IsDetected()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".reg");
        var content = "Windows Registry Editor Version 5.00\r\n\r\n[HKEY_CURRENT_USER\\Test]\r\n\"a\"=\"b\"\r\n";
        File.WriteAllBytes(path, Encoding.Unicode.GetBytes(content));
        try
        {
            var file = new RegFileParser().Parse(path);
            Assert.Equal("\"b\"", file.Data["HKEY_CURRENT_USER\\Test"]["a"]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Parse_EmptyAndCommentLines_AreIgnored()
    {
        var path = WriteTemp("""
Windows Registry Editor Version 5.00

; This is a comment, not a key

[HKEY_CURRENT_USER\Test]
"a"="b"


""");
        try
        {
            var parser = new RegFileParser();
            var file = parser.Parse(path);
            Assert.Single(file.Data);
            Assert.Equal("\"b\"", file.Data["HKEY_CURRENT_USER\\Test"]["a"]);
        }
        finally { File.Delete(path); }
    }

    // reg.exe writes a line break of the data as \r\n, and a \r\n of the data as \r\r\n: the data is kept
    // exactly as written (not one line break more).
    [Fact]
    public void Parse_StringLineBreaks_AreKeptAsWritten()
    {
        var path = WriteTemp("Windows Registry Editor Version 5.00\r\n\r\n[HKEY_CURRENT_USER\\Test]\r\n"
            + "\"Lf\"=\"a\r\nb\"\r\n\"CrLf\"=\"a\r\r\nb\"\r\n\"Next\"=\"x\"\r\n");
        try
        {
            var values = new RegFileParser().Parse(path).Data["HKEY_CURRENT_USER\\Test"];
            Assert.Equal("\"a\r\nb\"", values["Lf"]);
            Assert.Equal("\"a\r\r\nb\"", values["CrLf"]);
            Assert.Equal("\"x\"", values["Next"]);
            Assert.Equal("a\nb", RegTypeFormatter.GetDisplayValue(values["Lf"]));
        }
        finally { File.Delete(path); }
    }

    // Comments after the data, a comment after a key, comment and blank lines inside a hex value, a key
    // written with a trailing backslash: read as Registry Editor reads them.
    [Fact]
    public void Parse_CommentsAndKeyForms_AsRegistryEditorReadsThem()
    {
        var path = WriteTemp("Windows Registry Editor Version 5.00\r\n\r\n"
            + "[HKEY_CURRENT_USER\\A] ; the first key\r\n"
            + "\"s\"=\"Value1\"  ;comment\r\n"
            + "\"d\"=dword:00000004;comment\r\n"
            + "\"e\"=hex(2):25,00,50,00,00,00  ;comment\r\n"
            + "\"m\"=hex(7):4c,00,69,00,\\\r\n  ;comment\r\n\r\n  6e,00,65,00,00,00,00,00\r\n"
            + "\"h\"=hex:01,02,\\;comment\r\n  03\r\n"
            + "#\"Comment1\"=\"Value 1\"\r\nfoo=bar\r\n"
            + "[HKEY_CURRENT_USER\\B\\]\r\n\"y\"=\"2\"\r\n"
            + "[ HKEY_CURRENT_USER\\C]\r\n\"lost\"=\"1\"\r\n"
            + "[HKEY_CURRENT_USER\\D\r\n\"lost\"=\"2\"\r\n");
        try
        {
            var data = new RegFileParser().Parse(path).Data;
            Assert.Equal(["HKEY_CURRENT_USER\\A", "HKEY_CURRENT_USER\\B"], data.Keys.Order());
            var a = data["HKEY_CURRENT_USER\\A"];
            Assert.Equal(["d", "e", "h", "m", "s"], a.Keys.Order());
            Assert.Equal("\"Value1\"", a["s"]);
            Assert.Equal("dword:00000004", a["d"]);
            Assert.Equal("hex(2):25,00,50,00,00,00", a["e"]);
            Assert.Equal("hex(7):4c,00,69,00,6e,00,65,00,00,00,00,00", a["m"]);
            Assert.Equal("hex:01,02,03", a["h"]);
            Assert.Equal("Line", RegTypeFormatter.GetDisplayValue(a["m"]));
            Assert.Equal("\"2\"", data["HKEY_CURRENT_USER\\B"]["y"]);
        }
        finally { File.Delete(path); }
    }

    // A string broken by an unescaped backslash before its quote, or never closed: its line alone is
    // dropped and the lines below are read as usual. A string on several lines may end with a comment.
    // A key line may end with a comment holding "]", and a key name may hold "]" itself.
    [Fact]
    public void Parse_BrokenStringsAndBracketsInKeys()
    {
        var path = WriteTemp("Windows Registry Editor Version 5.00\r\n\r\n"
            + "[HKEY_CURRENT_USER\\A] ; see [docs]\r\n"
            + "\"Path\"=\"C:\\Dir\\\"\r\n"
            + "\"Next\"=\"x\"\r\n"
            + "\"Lines\"=\"a\r\nb\"  ; two lines\r\n"
            + "\"Open\"=\"never closed\r\n"
            + "[HKEY_CURRENT_USER\\Software\\[x]]\r\n"
            + "\"y\"=\"2\"\r\n");
        try
        {
            var data = new RegFileParser().Parse(path).Data;
            Assert.Equal(["HKEY_CURRENT_USER\\A", "HKEY_CURRENT_USER\\Software\\[x]"], data.Keys.Order());
            var a = data["HKEY_CURRENT_USER\\A"];
            Assert.Equal(["Lines", "Next"], a.Keys.Order());
            Assert.Equal("\"x\"", a["Next"]);
            Assert.Equal("\"a\r\nb\"", a["Lines"]);
            Assert.Equal("\"2\"", data["HKEY_CURRENT_USER\\Software\\[x]"]["y"]);
        }
        finally { File.Delete(path); }
    }

    // The line ending most lines use is the file's: \n alone marks a file whose lines were converted.
    [Fact]
    public void Parse_LineEndings_AreTheHeaders()
    {
        var lf = WriteTemp("Windows Registry Editor Version 5.00\n\n[HKEY_CURRENT_USER\\T]\n\"s\"=\"x\"\n");
        var crlf = WriteTemp("Windows Registry Editor Version 5.00\r\n\r\n[HKEY_CURRENT_USER\\T]\r\n\"s\"=\"x\"\r\n");
        try
        {
            Assert.True(new RegFileParser().Parse(lf).LfLines);
            Assert.False(new RegFileParser().Parse(crlf).LfLines);
            // A header added by a script with another ending does not decide.
            File.WriteAllText(lf, "Windows Registry Editor Version 5.00\n\r\n[HKEY_CURRENT_USER\\T]\r\n\"s\"=\"x\"\r\n");
            Assert.False(new RegFileParser().Parse(lf).LfLines);
            // A converted file whose strings hold \r\n data: those breaks are data, not line endings.
            var strings = string.Concat(Enumerable.Range(0, 5).Select(static i => $"\"v{i}\"=\"a\r\nb\"\n"));
            File.WriteAllText(lf, "Windows Registry Editor Version 5.00\n\n[HKEY_CURRENT_USER\\T]\n" + strings);
            Assert.True(new RegFileParser().Parse(lf).LfLines);
        }
        finally
        {
            File.Delete(lf);
            File.Delete(crlf);
        }
    }
}
