using RegCompare.Models;
using RegCompare.Services;
using RegCompare.ViewModels;
using Xunit;

namespace RegCompare.Tests;

public class ValueRowsTextTests
{
    private static ValueRowViewModel Row(string name, string type, string data) => new()
    {
        Name = name,
        Type = type,
        DisplayValue = data,
        Status = DiffStatus.Identical,
        IsChange = false,
    };

    // One line per row, tab-separated; a field with a line break, a tab or a quote stays in one
    // spreadsheet cell: in double quotes, its own doubled.
    [Fact]
    public void Rows_AreTabSeparated_AndQuotedWhenNeeded()
    {
        var text = ValueRowsText.Rows(
        [
            Row("", "SZ", "plain"),
            Row("Multi", "MULTI_SZ", "ab\ncd"),
            Row("Say", "SZ", "a \"word\""),
        ]);

        Assert.Equal("(Default)\tSZ\tplain\nMulti\tMULTI_SZ\t\"ab\ncd\"\nSay\tSZ\t\"a \"\"word\"\"\"", text);
    }

    // Names read as in Registry Editor: "(Default)" for the default value, no .reg escapes.
    [Theory]
    [InlineData("", "(Default)")]
    [InlineData("(Default)", "(Default)")]
    [InlineData("C:\\\\Windows\\\\notepad.exe", "C:\\Windows\\notepad.exe")]
    [InlineData("say \\\"hi\\\"", "say \"hi\"")]
    public void DisplayName_IsTheNameAsRegistryEditorShowsIt(string name, string shown)
    {
        Assert.Equal(shown, ValueRowViewModel.DisplayNameOf(name));
    }

    // A single name or datum is copied as it is; several, one per line, quoted when needed.
    [Fact]
    public void NamesAndData_OneValueAsItIs_SeveralQuotedWhenNeeded()
    {
        Assert.Equal("ab\ncd", ValueRowsText.Data([Row("Multi", "MULTI_SZ", "ab\ncd")]));
        Assert.Equal("x\n\"ab\ncd\"", ValueRowsText.Data([Row("A", "SZ", "x"), Row("Multi", "MULTI_SZ", "ab\ncd")]));
        Assert.Equal("(Default)\nA", ValueRowsText.Names([Row("", "SZ", "x"), Row("A", "SZ", "y")]));
    }
}
