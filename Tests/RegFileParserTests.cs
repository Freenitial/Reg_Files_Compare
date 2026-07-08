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
            Assert.Equal("\"Default value here\"", values["(Default)"]);
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

    [Fact]
    public void Parse_AnsiFileWithoutBom_FallsBackToLatin1()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".reg");
        var content = "REGEDIT4\r\n\r\n[HKEY_CURRENT_USER\\Test]\r\n\"accent\"=\"café\"\r\n";
        File.WriteAllBytes(path, Encoding.Latin1.GetBytes(content));
        try
        {
            var file = new RegFileParser().Parse(path);
            Assert.Equal("\"café\"", file.Data["HKEY_CURRENT_USER\\Test"]["accent"]);
        }
        finally { File.Delete(path); }
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
}
