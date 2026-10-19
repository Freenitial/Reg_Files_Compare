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

    // Keys in tree order: a key whose parent has no header of its own stays under that parent, before
    // the parent's next sibling ("A\X" before "AB", where '\' sorts after the letters).
    [Fact]
    public void SortedKeys_AreInTreeOrder()
    {
        var file = MakeFile("f.reg", new() { ["AB"] = new(), ["A\\X"] = new(), ["a\\x\\Y"] = new(), ["A B"] = new(), ["B"] = new() });
        var diff = new DiffEngine([file], 0);
        Assert.Equal(["A\\X", "a\\x\\Y", "A B", "AB", "B"], diff.SortedKeys);
        Assert.Equal(0, KeyPathComparer.Instance.Compare("HKCU\\Software", "hkcu\\SOFTWARE"));
    }

    // The reference column's bold: a key whose value differs in one other file, and its ancestors.
    [Fact]
    public void ChangedAnywhereForKey_MarksTheKeyAndItsAncestors_WhenAnyFileDiffers()
    {
        var refFile = MakeFile("ref.reg", new() { ["Root"] = new(), ["Root\\K"] = new() { ["v"] = "\"a\"" }, ["Root\\Same"] = new() });
        var b = MakeFile("b.reg", new() { ["Root"] = new(), ["Root\\K"] = new() { ["v"] = "\"a\"" }, ["Root\\Same"] = new() });
        var c = MakeFile("c.reg", new() { ["Root"] = new(), ["Root\\K"] = new() { ["v"] = "\"DIFFERENT\"" }, ["Root\\Same"] = new() });

        var diff = new DiffEngine([refFile, b, c], 0);
        Assert.True(diff.ChangedAnywhereForKey("Root\\K"));
        Assert.True(diff.ChangedAnywhereForKey("Root"));
        Assert.False(diff.ChangedAnywhereForKey("Root\\Same"));
    }

    [Fact]
    public void ChangedAnywhereForKey_FalseWhenAllFilesIdentical()
    {
        var refFile = MakeFile("ref.reg", new() { ["K"] = new() { ["v"] = "\"a\"" } });
        var b = MakeFile("b.reg", new() { ["K"] = new() { ["v"] = "\"a\"" } });

        var diff = new DiffEngine([refFile, b], 0);
        Assert.False(diff.ChangedAnywhereForKey("K"));
    }

    // A key missing from another file counts as a change for the reference.
    [Fact]
    public void ChangedAnywhereForKey_TrueForAKeyMissingElsewhere()
    {
        var refFile = MakeFile("ref.reg", new() { ["K"] = new() });
        var b = MakeFile("b.reg", new() { ["Other"] = new() });

        var diff = new DiffEngine([refFile, b], 0);
        Assert.True(diff.ChangedAnywhereForKey("K"));
    }

    // The reference column's bold for a value: added, missing or different in at least one other file.
    [Fact]
    public void ValueChangedAnywhere_DetectsEachKindOfChange()
    {
        var refFile = MakeFile("ref.reg", new() { ["K"] = new() { ["same"] = "\"x\"", ["differ"] = "\"a\"", ["gone"] = "\"g\"" } });
        var b = MakeFile("b.reg", new() { ["K"] = new() { ["same"] = "\"x\"", ["differ"] = "\"a\"", ["gone"] = "\"g\"" } });
        var c = MakeFile("c.reg", new() { ["K"] = new() { ["same"] = "\"x\"", ["differ"] = "\"b\"", ["new"] = "\"n\"" } });

        var diff = new DiffEngine([refFile, b, c], 0);
        Assert.False(diff.ValueChangedAnywhere("K", "same"));
        Assert.True(diff.ValueChangedAnywhere("K", "differ"));
        Assert.True(diff.ValueChangedAnywhere("K", "gone"));
        Assert.True(diff.ValueChangedAnywhere("K", "new"));
    }

    // Names are compared without regard to case, as the registry does; data is compared exactly.
    [Fact]
    public void ValueStatus_NamesIgnoreCase_DataDoesNot()
    {
        // Dictionaries as the parser builds them.
        static Dictionary<string, Dictionary<string, string>> Data(string key, string name, string raw) =>
            new(System.StringComparer.OrdinalIgnoreCase) { [key] = new(System.StringComparer.OrdinalIgnoreCase) { [name] = raw } };
        var refFile = MakeFile("ref.reg", Data("K", "Name", "\"Data\""));
        var other = MakeFile("other.reg", Data("k", "NAME", "\"data\""));

        var diff = new DiffEngine([refFile, other], 0);
        Assert.Equal(DiffStatus.Exists, diff.GetKeyStatus("K", 1));
        Assert.Equal(DiffStatus.Different, diff.GetValueStatus("K", "name", 1));
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
