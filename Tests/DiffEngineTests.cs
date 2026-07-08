using System.Collections.Generic;
using RegCompare.Models;
using RegCompare.Services;
using Xunit;

namespace RegCompare.Tests;

public class DiffEngineTests
{
    private static RegFile MakeFile(string name, Dictionary<string, Dictionary<string, string>> data) => new()
    {
        Path = $@"C:\fake\{name}",
        FileName = name,
        Modified = System.DateTime.UtcNow,
        SizeBytes = 100,
        Data = data,
    };

    [Fact]
    public void GetKeyStatus_ReferenceFile_ReturnsReferenceWhenPresent()
    {
        var refFile = MakeFile("ref.reg", new() { ["A"] = new() { ["v"] = "\"x\"" } });
        var diff = new DiffEngine([refFile], 0);
        Assert.Equal(DiffStatus.Reference, diff.GetKeyStatus("A", 0));
        Assert.Equal(DiffStatus.Hidden, diff.GetKeyStatus("Missing", 0));
    }

    [Fact]
    public void GetKeyStatus_NonReference_DetectsMissingAddedExists()
    {
        var refFile = MakeFile("ref.reg", new() { ["KeyA"] = new(), ["KeyB"] = new() });
        var other = MakeFile("other.reg", new() { ["KeyA"] = new(), ["KeyC"] = new() });

        var diff = new DiffEngine([refFile, other], 0);
        Assert.Equal(DiffStatus.Exists, diff.GetKeyStatus("KeyA", 1));
        Assert.Equal(DiffStatus.Missing, diff.GetKeyStatus("KeyB", 1));
        Assert.Equal(DiffStatus.Added, diff.GetKeyStatus("KeyC", 1));
    }

    [Fact]
    public void GetValueStatus_DetectsAllStates()
    {
        var refFile = MakeFile("ref.reg", new()
        {
            ["K"] = new() { ["same"] = "\"same\"", ["differ"] = "\"a\"", ["onlyref"] = "\"r\"" }
        });
        var other = MakeFile("other.reg", new()
        {
            ["K"] = new() { ["same"] = "\"same\"", ["differ"] = "\"b\"", ["onlyother"] = "\"o\"" }
        });
        var diff = new DiffEngine([refFile, other], 0);

        Assert.Equal(DiffStatus.Identical, diff.GetValueStatus("K", "same", 1));
        Assert.Equal(DiffStatus.Different, diff.GetValueStatus("K", "differ", 1));
        Assert.Equal(DiffStatus.Missing, diff.GetValueStatus("K", "onlyref", 1));
        Assert.Equal(DiffStatus.Added, diff.GetValueStatus("K", "onlyother", 1));
    }

    [Fact]
    public void KeyChangedAnywhere_TrueIfAnyFileDiffers()
    {
        var refFile = MakeFile("ref.reg", new() { ["K"] = new() { ["v"] = "\"a\"" } });
        var b = MakeFile("b.reg", new() { ["K"] = new() { ["v"] = "\"a\"" } });
        var c = MakeFile("c.reg", new() { ["K"] = new() { ["v"] = "\"DIFFERENT\"" } });

        var diff = new DiffEngine([refFile, b, c], 0);
        Assert.True(diff.KeyChangedAnywhere("K"));
    }

    [Fact]
    public void KeyChangedAnywhere_FalseWhenAllFilesIdentical()
    {
        var refFile = MakeFile("ref.reg", new() { ["K"] = new() { ["v"] = "\"a\"" } });
        var b = MakeFile("b.reg", new() { ["K"] = new() { ["v"] = "\"a\"" } });

        var diff = new DiffEngine([refFile, b], 0);
        Assert.False(diff.KeyChangedAnywhere("K"));
    }

    [Fact]
    public void KeyOrSubtreeChanged_DetectsValueDiffInSubKey()
    {
        var refFile = MakeFile("ref.reg", new()
        {
            ["Root"] = new(),
            ["Root\\Sub"] = new() { ["v"] = "\"a\"" },
        });
        var other = MakeFile("other.reg", new()
        {
            ["Root"] = new(),
            ["Root\\Sub"] = new() { ["v"] = "\"DIFF\"" },
        });
        var diff = new DiffEngine([refFile, other], 0);

        Assert.True(diff.KeyOrSubtreeChanged("Root", 1));
    }
}
