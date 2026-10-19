using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RegCompare.Models;
using RegCompare.Services;
using Xunit;

namespace RegCompare.Tests;

public class RegFileWriterTests
{
    private static RegFile MakeFile(Dictionary<string, Dictionary<string, string>> data, bool regedit4 = false) => new()
    {
        Path = @"C:\fake\file.reg",
        FileName = "file.reg",
        Modified = DateTime.UtcNow,
        SizeBytes = 100,
        Data = data,
        IsRegedit4 = regedit4,
    };

    // Dictionaries as the parser builds them (names without regard to case).
    private static Dictionary<string, Dictionary<string, string>> Data(params (string Key, (string Name, string Raw)[] Values)[] keys)
    {
        var data = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, values) in keys)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, raw) in values) map[name] = raw;
            data[key] = map;
        }
        return data;
    }

    private static string[] Lines(string text) => text.Split("\r\n");

    // Every parent key is declared (empty), then the key with the chosen values.
    [Fact]
    public void BuildFromValues_DeclaresTheParents()
    {
        var file = MakeFile(Data(("HKEY_CURRENT_USER\\A\\B", [("v", "\"x\""), ("w", "\"y\"")])));
        var lines = Lines(RegFileWriter.BuildFromValues(file, "HKEY_CURRENT_USER\\A\\B", ["v"]));

        Assert.Equal(
        [
            "Windows Registry Editor Version 5.00", "",
            "[HKEY_CURRENT_USER]", "[HKEY_CURRENT_USER\\A]", "[HKEY_CURRENT_USER\\A\\B]", "\"v\"=\"x\"", "", "",
        ], lines);
    }

    // A deletion entry needs no parent: "[-HKEY_CURRENT_USER]" would delete the whole hive on import.
    [Fact]
    public void BuildFromSubtree_DeletionEntry_WritesNoParentDeletions()
    {
        var file = MakeFile(Data(("-HKEY_CURRENT_USER\\Software\\Contoso\\OldApp", [])));

        foreach (var root in new[] { "-HKEY_CURRENT_USER", "-HKEY_CURRENT_USER\\Software", "-HKEY_CURRENT_USER\\Software\\Contoso\\OldApp" })
        {
            var lines = Lines(RegFileWriter.BuildFromSubtree(file, root));
            var headers = lines.Where(static l => l.StartsWith('[')).ToList();
            Assert.Equal(["[-HKEY_CURRENT_USER\\Software\\Contoso\\OldApp]"], headers);
        }
    }

    // "@" for the default value (empty name), quotes for a value named "(Default)".
    [Fact]
    public void BuildFromSubtree_WritesTheDefaultValueAsAt()
    {
        var file = MakeFile(Data(("HKEY_CURRENT_USER\\K", [("", "\"d\""), ("(Default)", "\"named\"")])));
        var lines = Lines(RegFileWriter.BuildFromSubtree(file, "HKEY_CURRENT_USER\\K"));

        Assert.Contains("@=\"d\"", lines);
        Assert.Contains("\"(Default)\"=\"named\"", lines);
    }

    // The file's own spelling of the key, its parents and its values, not the one the columns show.
    [Fact]
    public void Exports_UseTheFilesOwnSpelling()
    {
        var file = MakeFile(Data(("HKEY_LOCAL_MACHINE\\SOFTWARE\\Contoso\\BROWSER", [("homepagelocation", "\"x\"")])));

        var values = Lines(RegFileWriter.BuildFromValues(file, "HKEY_LOCAL_MACHINE\\Software\\Contoso\\Browser", ["HomepageLocation"]));
        Assert.Contains("[HKEY_LOCAL_MACHINE\\SOFTWARE\\Contoso]", values);
        Assert.Contains("[HKEY_LOCAL_MACHINE\\SOFTWARE\\Contoso\\BROWSER]", values);
        Assert.Contains("\"homepagelocation\"=\"x\"", values);

        var tree = Lines(RegFileWriter.BuildFromSubtree(file, "HKEY_LOCAL_MACHINE\\Software\\contoso"));
        Assert.Contains("[HKEY_LOCAL_MACHINE\\SOFTWARE]", tree);
        Assert.Contains("[HKEY_LOCAL_MACHINE\\SOFTWARE\\Contoso\\BROWSER]", tree);
        Assert.DoesNotContain("[HKEY_LOCAL_MACHINE\\Software]", tree);
    }

    // A REGEDIT4 file exports as REGEDIT4, in ANSI: its hex(2)/hex(7) bytes stay right.
    [Fact]
    public void Regedit4File_ExportsAsRegedit4InAnsi()
    {
        var file = MakeFile(Data(("HKEY_CURRENT_USER\\K", [("Path", "hex(2):25,50,41,54,48,25,00")])), regedit4: true);

        var text = RegFileWriter.BuildFromSubtree(file, "HKEY_CURRENT_USER\\K");
        Assert.StartsWith("REGEDIT4\r\n", text, StringComparison.Ordinal);

        // ANSI, without any byte-order mark (even when the system code page is UTF-8).
        var bytes = RegFileWriter.Encode(file, text);
        Assert.Equal((byte)'R', bytes[0]);
        Assert.Equal(text, AnsiEncoding.Current.GetString(bytes));

        // A version 5 export: UTF-16LE with its byte-order mark.
        Assert.Equal(new byte[] { 0xFF, 0xFE, (byte)'x', 0 }, RegFileWriter.Encode(MakeFile(Data()), "x"));

        // A character the ANSI code page cannot write is refused, not replaced by a look-alike.
        if (AnsiEncoding.Current.CodePage != 65001)
            Assert.Throws<EncoderFallbackException>(() => RegFileWriter.Encode(file, "\"Ω 日本 \U0001F600\""));
    }

    [Fact]
    public void HasSubtree_TellsWhetherTheFileHasTheKeyOrOneBelow()
    {
        var file = MakeFile(Data(("HKEY_CURRENT_USER\\A\\B", [])));
        Assert.True(RegFileWriter.HasSubtree(file, "HKEY_CURRENT_USER\\A"));
        Assert.True(RegFileWriter.HasSubtree(file, "hkey_current_user\\a\\b"));
        Assert.False(RegFileWriter.HasSubtree(file, "HKEY_CURRENT_USER\\A\\B\\C"));
        Assert.False(RegFileWriter.HasSubtree(file, "HKEY_CURRENT_USER\\AB"));
    }

    // Exported, then read again: the same keys and values (a string on several lines included).
    [Fact]
    public void Export_ReadsBackTheSame()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".reg");
        var exported = path + ".export.reg";
        File.WriteAllText(path,
            "Windows Registry Editor Version 5.00\r\n\r\n[HKEY_CURRENT_USER\\Test]\r\n@=\"d\"\r\n"
            + "\"Tip\"=\"line 1\r\nline 2\"\r\n\"Bin\"=hex:01,02,\\\r\n  03\r\n\"N\"=dword:0000000a\r\n\r\n"
            + "[HKEY_CURRENT_USER\\Test\\Sub]\r\n\"v\"=\"x\"\r\n", Encoding.Unicode);
        try
        {
            var parser = new RegFileParser();
            var file = parser.Parse(path);
            File.WriteAllBytes(exported, RegFileWriter.Encode(file, RegFileWriter.BuildFromSubtree(file, "HKEY_CURRENT_USER\\Test")));
            var again = parser.Parse(exported);

            Assert.Equal(file.Data.Keys.Order(), again.Data.Keys.Where(k => file.Data.ContainsKey(k)).Order());
            foreach (var (key, values) in file.Data)
            {
                Assert.Equal(values.OrderBy(static kv => kv.Key), again.Data[key].OrderBy(static kv => kv.Key));
            }
        }
        finally
        {
            File.Delete(path);
            File.Delete(exported);
        }
    }

    // The strings of a file whose lines end with \n are written as reg.exe writes them: read back from
    // the export, they hold the same data (a \r\n of the data included).
    [Fact]
    public void Export_OfAConvertedFile_KeepsItsStrings()
    {
        var source = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".reg");
        var export = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".reg");
        File.WriteAllText(source, "Windows Registry Editor Version 5.00\n\n[HKEY_CURRENT_USER\\K]\n\"Crlf\"=\"a\r\nb\"\n\"Lf\"=\"a\nb\"\n");
        try
        {
            var parser = new RegFileParser();
            var file = parser.Parse(source);
            Assert.True(file.LfLines);
            File.WriteAllBytes(export, RegFileWriter.Encode(file, RegFileWriter.BuildFromSubtree(file, "HKEY_CURRENT_USER\\K")));
            var back = parser.Parse(export);
            Assert.False(back.LfLines);
            foreach (var name in new[] { "Crlf", "Lf" })
            {
                var before = file.Data["HKEY_CURRENT_USER\\K"][name];
                var after = back.Data["HKEY_CURRENT_USER\\K"][name];
                Assert.True(RegData.Same(before, false, after, false, file.LfLines, back.LfLines), name);
            }
        }
        finally
        {
            File.Delete(source);
            File.Delete(export);
        }
    }
}
