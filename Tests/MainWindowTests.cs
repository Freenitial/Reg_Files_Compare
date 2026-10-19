using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging.Abstractions;
using RegCompare.Services;
using RegCompare.ViewModels;
using RegCompare.Views;
using Xunit;

namespace RegCompare.Tests;

/// <summary>
/// The application without a screen: the real views, driven as a user would, with real text (Skia
/// and the Inter font, as the application draws it).
/// </summary>
public static class HeadlessApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .WithInterFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>One Avalonia application per process: every window test shares it.</summary>
public sealed class HeadlessSession : IDisposable
{
    public HeadlessUnitTestSession Session { get; } = HeadlessUnitTestSession.StartNew(typeof(HeadlessApp));

    public void Dispose() => Session.Dispose();
}

[CollectionDefinition("Headless")]
public sealed class HeadlessCollection : ICollectionFixture<HeadlessSession>;

/// <summary>
/// The main window with three exports: when the comparison is made again, and what stays selected.
/// </summary>
[Collection("Headless")]
public sealed class MainWindowTests : IDisposable
{
    private const string Contoso = @"HKEY_LOCAL_MACHINE\SOFTWARE\Contoso";
    private const string Browser = @"HKEY_LOCAL_MACHINE\SOFTWARE\Contoso\Browser";
    private const string Extra = @"HKEY_LOCAL_MACHINE\SOFTWARE\Contoso\Extra";

    private readonly HeadlessSession _headless;
    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;
    private readonly string _baseline;
    private readonly string _ws1;
    private readonly string _ws2;

    public MainWindowTests(HeadlessSession headless)
    {
        _headless = headless;
        _baseline = Write("baseline.reg", $"""
            [{Contoso}]
            "Version"="1.0"

            [{Browser}]
            @="b"
            "HomepageLocation"="https://intranet"
            "Same"="s"
            """);
        // A different home page and default value, and an added key without values.
        _ws1 = Write("ws1.reg", $"""
            [{Contoso}]
            "Version"="1.0"

            [{Browser}]
            @="w"
            "HomepageLocation"="https://example.com"
            "Same"="s"

            [{Extra}]
            """);
        // Only the version differs.
        _ws2 = Write("ws2.reg", $"""
            [{Contoso}]
            "Version"="2.0"

            [{Browser}]
            @="b"
            "HomepageLocation"="https://intranet"
            "Same"="s"
            """);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Write(string name, string body)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "Windows Registry Editor Version 5.00\r\n\r\n" + body.ReplaceLineEndings("\r\n") + "\r\n");
        return path;
    }

    private Task Run(Func<Task> test) => _headless.Session.Dispatch(async () =>
    {
        await test();
        return true;
    }, CancellationToken.None);

    private static (MainWindowViewModel Vm, MainWindow Window) Open(params string[] files)
    {
        var vm = new MainWindowViewModel(new RegFileParser(), new DragDropService(), new ThemeService(), NullLogger<MainWindowViewModel>.Instance);
        var window = new MainWindow { DataContext = vm, Width = 1600, Height = 900 };
        window.Show();
        foreach (var file in files)
        {
            var added = vm.AddRegFileAsync(file);
            for (var i = 0; i < 1000 && !added.IsCompleted; i++) { Thread.Sleep(5); Pump(window); }
            Assert.True(added.Result);
        }
        Pump(window);
        return (vm, window);
    }

    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Wait until no comparison runs (a replayed one included), then let the views catch up.</summary>
    private static async Task Idle(MainWindowViewModel vm, Window window)
    {
        for (var round = 0; round < 2; round++)
        {
            for (var i = 0; i < 1000 && (vm.IsComparing || vm.IsLoading); i++) await Task.Delay(10);
            await Task.Delay(30);
            Pump(window);
        }
        Assert.False(vm.IsComparing);
    }

    private static async Task Compare(MainWindowViewModel vm, Window window)
    {
        await vm.CompareCommand.ExecuteAsync(null);
        await Idle(vm, window);
    }

    private static List<FileColumnViewModel> FileColumns(MainWindowViewModel vm) => vm.Columns.OfType<FileColumnViewModel>().ToList();

    private static FileColumnView ViewOf(Window window, ColumnViewModelBase column) =>
        window.GetVisualDescendants().OfType<FileColumnView>().Single(v => ReferenceEquals(v.DataContext, column));

    private static KeyNodeViewModel? Find(IEnumerable<KeyNodeViewModel> nodes, string path)
    {
        foreach (var node in nodes)
        {
            if (string.Equals(node.Path, path, StringComparison.OrdinalIgnoreCase)) return node;
            if (Find(node.Children, path) is { } below) return below;
        }
        return null;
    }

    /// <summary>Select a key, then a value, in a file column, as clicks do.</summary>
    private static void Select(Window window, FileColumnViewModel column, string key, string? value)
    {
        var view = ViewOf(window, column);
        // A click reaches a row its parents show: they are opened first, as the user would.
        var node = Find(column.Roots, key)!;
        for (var parent = node.Parent; parent is not null; parent = parent.Parent) parent.IsExpanded = true;
        Pump(window);
        view.FindControl<ListBox>("KeysTree")!.SelectedItem = node;
        Pump(window);
        if (value is null) return;
        view.FindControl<ListBox>("ValuesGrid")!.SelectedItem = column.ValueRows.Single(r => r.Name == value);
        Pump(window);
    }

    // Removing a file while a comparison runs: the comparison is replayed without it.
    [Fact]
    public Task RemovingAFileDuringAComparison_ReplaysIt() => Run(async () =>
    {
        var (vm, window) = Open(_baseline, _ws1);
        var running = vm.CompareCommand.ExecuteAsync(null);
        Assert.True(vm.IsComparing);
        vm.RemoveLoadedFile(vm.LoadedFiles[1]);
        await running;
        await Idle(vm, window);

        Assert.Empty(FileColumns(vm));
        Assert.Equal("Compared files = 0", vm.StatusText);
        window.Close();
    });

    // A reference chosen while a comparison runs is applied by the replay.
    [Fact]
    public Task ChoosingAReferenceDuringAComparison_IsApplied() => Run(async () =>
    {
        var (vm, window) = Open(_baseline, _ws1, _ws2);
        var running = vm.CompareCommand.ExecuteAsync(null);
        vm.ReferenceFile = vm.LoadedFiles[2];
        await running;
        await Idle(vm, window);

        Assert.Same(vm.LoadedFiles[2], vm.ReferenceFile);
        Assert.Equal("(REF) ws2.reg", FileColumns(vm)[0].Title);
        window.Close();
    });

    // Before any comparison, removing a file compares nothing.
    [Fact]
    public Task RemovingAFileBeforeAnyComparison_ComparesNothing() => Run(async () =>
    {
        var (vm, window) = Open(_baseline, _ws1, _ws2);
        vm.RemoveLoadedFile(vm.LoadedFiles[2]);
        await Idle(vm, window);

        Assert.Empty(FileColumns(vm));
        window.Close();
    });

    // Once compared, the display follows the boxes: unticked down to one file it goes, ticked again it
    // comes back, and dropped files join it.
    [Fact]
    public Task TheDisplayFollowsTheFileList() => Run(async () =>
    {
        var (vm, window) = Open(_baseline, _ws1);
        await Compare(vm, window);
        Assert.Equal(2, FileColumns(vm).Count);

        vm.LoadedFiles[1].IsChecked = false;
        await Idle(vm, window);
        Assert.Empty(FileColumns(vm));

        vm.LoadedFiles[1].IsChecked = true;
        await Idle(vm, window);
        Assert.Equal(2, FileColumns(vm).Count);

        await vm.HandleDroppedPathsAsync([_ws2]);
        await Idle(vm, window);
        Assert.Equal(3, FileColumns(vm).Count);
        Assert.Equal("Compared files = 3", vm.StatusText);
        window.Close();
    });

    // Toggling a filter or the theme keeps the selected key and value in every column.
    [Fact]
    public Task FiltersAndTheme_KeepTheSelection() => Run(async () =>
    {
        var (vm, window) = Open(_baseline, _ws1, _ws2);
        await Compare(vm, window);
        Select(window, FileColumns(vm)[0], Browser, "HomepageLocation");
        Assert.Equal(Browser, vm.SelectedKeyPath);
        Assert.Equal("HomepageLocation", vm.SelectedValueName);

        vm.ShowOnlyChanges = false;
        Pump(window);
        vm.HideEmptyAddedNodes = true;
        Pump(window);
        vm.Theme.IsDark = false;
        Pump(window);

        Assert.Equal(Browser, vm.SelectedKeyPath);
        Assert.Equal("HomepageLocation", vm.SelectedValueName);
        foreach (var column in FileColumns(vm))
        {
            Assert.Equal(Browser, column.SelectedNode?.Path);
            Assert.Equal("HomepageLocation", column.SelectedValueRow?.Name);
            Assert.Same(column.SelectedValueRow, ViewOf(window, column).FindControl<ListBox>("ValuesGrid")!.SelectedItem);
        }
        vm.Theme.IsDark = true;
        window.Close();
    });

    // Another key: no value stays selected; the default value (empty name) synchronizes like any other.
    [Fact]
    public Task ValueSelection_FollowsTheKey_AndTheDefaultValueSynchronizes() => Run(async () =>
    {
        var (vm, window) = Open(_baseline, _ws1);
        await Compare(vm, window);
        var columns = FileColumns(vm);
        Select(window, columns[0], Browser, "");
        Assert.Equal("", vm.SelectedValueName);
        Assert.Equal("", columns[1].SelectedValueRow?.Name);
        Assert.Equal("(Default)", columns[1].SelectedValueRow?.DisplayName);

        Select(window, columns[0], Contoso, null);
        Assert.Null(vm.SelectedValueName);
        Assert.All(columns, static c => Assert.Null(c.SelectedValueRow));
        window.Close();
    });

    // A search column follows the value selection, and drives it.
    [Fact]
    public Task SearchColumn_FollowsAndDrivesTheValueSelection() => Run(async () =>
    {
        var (vm, window) = Open(_baseline, _ws1);
        await Compare(vm, window);
        vm.SearchTerm = "Homepage";
        vm.AddSearchColumnCommand.Execute(null);
        Pump(window);
        var search = vm.Columns.OfType<SearchColumnViewModel>().Single();
        var columns = FileColumns(vm);
        Assert.Equal(Browser, vm.SelectedKeyPath);

        Select(window, columns[0], Browser, "HomepageLocation");
        Assert.Equal("HomepageLocation", search.SelectedValueRow?.Name);

        var searchView = window.GetVisualDescendants().OfType<SearchColumnView>().Single();
        searchView.FindControl<ListBox>("SearchValuesGrid")!.SelectedItem = null;
        Pump(window);
        Assert.Null(vm.SelectedValueName);
        searchView.FindControl<ListBox>("SearchValuesGrid")!.SelectedItem = search.ValueRows.Single(r => r.Name == "HomepageLocation");
        Pump(window);
        Assert.Equal("HomepageLocation", vm.SelectedValueName);
        Assert.Equal("HomepageLocation", columns[1].SelectedValueRow?.Name);
        window.Close();
    });

    // A key that "Hide empty added nodes" removed is not selected from elsewhere either.
    [Fact]
    public Task HiddenEmptyAddedKey_IsNotSelectedFromASearch() => Run(async () =>
    {
        var (vm, window) = Open(_baseline, _ws1);
        await Compare(vm, window);
        vm.HideEmptyAddedNodes = true;
        Pump(window);
        var ws1 = FileColumns(vm)[1];
        Assert.Null(Find(ws1.Roots, Extra));

        vm.SearchTerm = "Extra";
        vm.AddSearchColumnCommand.Execute(null);
        Pump(window);
        Assert.Equal(Extra, vm.SelectedKeyPath);
        Assert.Null(ws1.SelectedNode);
        Assert.Empty(ws1.ValueRows);
        window.Close();
    });

    // The Data column's width: the estimate picks the texts to measure, the result is exact.
    [Fact]
    public Task Widest_IsTheExactWidthOfTheWidestText() => Run(() =>
    {
        var random = new Random(7);
        const string letters = "iIl1.,WMm@%abcdefghXYZ 0x";
        var texts = Enumerable.Range(0, 300)
            .Select(_ => new string(Enumerable.Range(0, random.Next(1, 40)).Select(_ => letters[random.Next(letters.Length)]).ToArray()))
            .Append("first line\n" + new string('W', 30))
            .ToList();

        Assert.Equal(texts.Max(TextWidth.Measure), TextWidth.Widest(texts));
        Assert.Equal(TextWidth.Measure(new string('W', 30)), TextWidth.Measure("first line\n" + new string('W', 30)), 3);
        return Task.CompletedTask;
    });

    // The Data column is as wide as its widest datum drawn in the grid's own font (Inter, bold).
    [Fact]
    public Task DataColumn_FitsTheWidestDatumAsDrawn() => Run(async () =>
    {
        var wide = Write("wide.reg", $"""
            [{Browser}]
            "Long"="0x0000000000001000 (4096) NotificationsAllowedForUrls_00001000 WWWWWWWWWW"
            "Short"="x"
            """);
        var (vm, window) = Open(_baseline, wide);
        await Compare(vm, window);
        var column = FileColumns(vm)[1];
        Select(window, column, Browser, null);

        var cell = ViewOf(window, column).FindControl<ListBox>("ValuesGrid")!
            .GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == column.ValueRows.Single(r => r.Name == "Long").DisplayValue);
        var probe = new TextBlock
        {
            Text = cell.Text,
            FontFamily = cell.FontFamily,
            FontSize = cell.FontSize,
            FontWeight = Avalonia.Media.FontWeight.Bold,
            FontFeatures = cell.FontFeatures,
            Padding = cell.Padding,
        };
        probe.Measure(Size.Infinity);
        Assert.InRange(column.DataColumnWidth.Value, probe.DesiredSize.Width, probe.DesiredSize.Width + 8);
        window.Close();
    });

    // A search column follows the value selection only while it shows the key selected elsewhere, and
    // a value picked there takes the columns to its key first.
    [Fact]
    public Task SearchColumn_KeepsValuesToTheirKey() => Run(async () =>
    {
        var (vm, window) = Open(_baseline, _ws1);
        await Compare(vm, window);
        vm.SearchTerm = "Homepage";
        vm.AddSearchColumnCommand.Execute(null);
        Pump(window);
        var search = vm.Columns.OfType<SearchColumnViewModel>().Single();
        var columns = FileColumns(vm);

        Select(window, columns[0], Contoso, "Version");
        Assert.Null(search.SelectedValueRow);

        var searchGrid = window.GetVisualDescendants().OfType<SearchColumnView>().Single().FindControl<ListBox>("SearchValuesGrid")!;
        searchGrid.SelectedItem = search.ValueRows.Single(r => r.Name == "HomepageLocation");
        Pump(window);
        Assert.Equal(Browser, vm.SelectedKeyPath);
        Assert.Equal("HomepageLocation", vm.SelectedValueName);
        Assert.Equal(Browser, columns[0].SelectedNode?.Path);
        Assert.Equal("HomepageLocation", columns[1].SelectedValueRow?.Name);
        window.Close();
    });

    // A comparison made again keeps the selection, and the search columns where they are: one opened
    // while the columns are built stays after them, one closed meanwhile stays closed.
    [Fact]
    public Task ComparingAgain_KeepsSelectionAndSearchColumns() => Run(async () =>
    {
        var (vm, window) = Open(_baseline, _ws1);
        await Compare(vm, window);
        Select(window, FileColumns(vm)[0], Browser, "HomepageLocation");

        await vm.HandleDroppedPathsAsync([_ws2]);
        await Idle(vm, window);
        Assert.Equal(3, FileColumns(vm).Count);
        Assert.Equal(Browser, vm.SelectedKeyPath);
        Assert.Equal(Browser, FileColumns(vm)[0].SelectedNode?.Path);
        Assert.Equal(Browser, FileColumns(vm)[1].SelectedNode?.Path);
        Assert.Null(FileColumns(vm)[2].SelectedNode);   // ws2.reg: identical there, hidden by Show only changes
        Assert.Equal("HomepageLocation", FileColumns(vm)[1].SelectedValueRow?.Name);

        var running = vm.CompareCommand.ExecuteAsync(null);
        vm.SearchTerm = "Version";
        vm.AddSearchColumnCommand.Execute(null);
        await running;
        await Idle(vm, window);
        Assert.All(vm.Columns.Take(3), static c => Assert.IsType<FileColumnViewModel>(c));
        Assert.IsType<SearchColumnViewModel>(vm.Columns[3]);

        running = vm.CompareCommand.ExecuteAsync(null);
        vm.RemoveColumn(vm.Columns.OfType<SearchColumnViewModel>().Single());
        await running;
        await Idle(vm, window);
        Assert.Empty(vm.Columns.OfType<SearchColumnViewModel>());
        Assert.Equal(3, FileColumns(vm).Count);
        window.Close();
    });

    // Files dropped while a comparison runs: their load messages stay beside the count of the replay.
    [Fact]
    public Task LoadProblems_SurviveAReplay() => Run(async () =>
    {
        var empty = Write("empty2.reg", "");
        var (vm, window) = Open(_baseline, _ws1);
        await Compare(vm, window);

        var running = vm.CompareCommand.ExecuteAsync(null);
        await vm.HandleDroppedPathsAsync([empty, _ws2]);
        await running;
        await Idle(vm, window);
        Assert.StartsWith("Compared files = 4", vm.StatusText, StringComparison.Ordinal);
        Assert.Contains($"No registry key in: {empty}", vm.StatusText, StringComparison.Ordinal);
        window.Close();
    });

    // A file without keys is loaded and said so; the comparison keeps that beside its count.
    [Fact]
    public Task LoadProblems_StayAfterTheComparison() => Run(async () =>
    {
        var empty = Write("empty.reg", "");
        var vm = new MainWindowViewModel(new RegFileParser(), new DragDropService(), new ThemeService(), NullLogger<MainWindowViewModel>.Instance);
        var window = new MainWindow { DataContext = vm, Width = 1600, Height = 900 };
        window.Show();
        await vm.OpenAtStartAsync([_baseline, _ws1, empty]);
        await Idle(vm, window);

        Assert.StartsWith("Compared files = 3", vm.StatusText, StringComparison.Ordinal);
        Assert.Contains($"No registry key in: {empty}", vm.StatusText, StringComparison.Ordinal);
        window.Close();
    });

    private static void Press(Window window, PhysicalKey key)
    {
        window.KeyPressQwerty(key, RawInputModifiers.None);
        window.KeyReleaseQwerty(key, RawInputModifiers.None);
        Pump(window);
    }

    // The arrow keys move in the key tree as in a tree: Right opens a key, then goes to its first child;
    // Left goes to the parent, or closes an open key; Up / Down go on from the row reached.
    [Fact]
    public Task ArrowKeys_MoveInTheTree() => Run(async () =>
    {
        var (vm, window) = Open(_baseline, _ws1);
        await Compare(vm, window);
        vm.CollapseAllCommand.Execute(null);
        Pump(window);
        var column = FileColumns(vm)[0];
        var list = ViewOf(window, column).FindControl<ListBox>("KeysTree")!;
        var hive = column.Rows[0];
        list.SelectedItem = hive;
        Pump(window);
        list.ContainerFromItem(hive)!.Focus(NavigationMethod.Directional);

        Press(window, PhysicalKey.ArrowRight);
        Assert.True(hive.IsExpanded);
        Assert.Same(hive, column.SelectedNode);
        Press(window, PhysicalKey.ArrowRight);
        Assert.Equal(@"HKEY_LOCAL_MACHINE\SOFTWARE", column.SelectedNode!.Path);
        Press(window, PhysicalKey.ArrowRight);
        Press(window, PhysicalKey.ArrowDown);
        Assert.Equal(Contoso, column.SelectedNode!.Path);
        Assert.Same(column.SelectedNode, list.SelectedItem);
        Press(window, PhysicalKey.ArrowLeft);
        Assert.Equal(@"HKEY_LOCAL_MACHINE\SOFTWARE", column.SelectedNode!.Path);
        Press(window, PhysicalKey.ArrowLeft);
        Assert.False(column.SelectedNode!.IsExpanded);
        Press(window, PhysicalKey.ArrowUp);
        Assert.Same(hive, column.SelectedNode);
        window.Close();
    });

    // A Ctrl+click on the selected key clears the list's selection while the columns keep the key: the
    // list selects it again.
    [Fact]
    public Task DeselectingTheSelectedKey_SelectsItAgain() => Run(async () =>
    {
        var (vm, window) = Open(_baseline, _ws1);
        await Compare(vm, window);
        var column = FileColumns(vm)[0];
        Select(window, column, Browser, null);
        var list = ViewOf(window, column).FindControl<ListBox>("KeysTree")!;

        list.SelectedItem = null;
        Pump(window);
        Assert.Equal(Browser, column.SelectedNode!.Path, ignoreCase: true);
        Assert.Same(column.SelectedNode, list.SelectedItem);
        window.Close();
    });

    // A key whose parent is collapsed keeps its selection in the column, hidden: the list selects no
    // row (the tree menu then has nothing to act on).
    [Fact]
    public Task HiddenSelectedKey_SelectsNoRow() => Run(async () =>
    {
        var (vm, window) = Open(_baseline, _ws1);
        await Compare(vm, window);
        var column = FileColumns(vm)[0];
        Select(window, column, Browser, null);
        var list = ViewOf(window, column).FindControl<ListBox>("KeysTree")!;

        column.SelectedNode!.Parent!.IsExpanded = false;
        Pump(window);
        Assert.Null(list.SelectedItem);
        Assert.Equal(Browser, column.SelectedNode!.Path, ignoreCase: true);
        window.Close();
    });

    // The key tree is as wide as its widest row, not as the widest on screen: its horizontal scrolling
    // stays the same while it scrolls up and down.
    [Fact]
    public Task TreeWidth_StaysTheSame_WhileScrolling() => Run(async () =>
    {
        var deep = string.Join("\\", Enumerable.Range(0, 10).Select(static i => $"Key{i}")) + "\\" + new string('W', 80);
        var keys = $"[HKEY_CURRENT_USER\\A\\{deep}]\n\"v\"=\"1\"\n"
            + string.Concat(Enumerable.Range(0, 400).Select(static i => $"[HKEY_CURRENT_USER\\Z\\K{i:000}]\n\"v\"=\"1\"\n"));
        var a = Write("wide-a.reg", keys);
        var b = Write("wide-b.reg", keys.Replace("\"v\"=\"1\"", "\"v\"=\"2\"", StringComparison.Ordinal));
        var (vm, window) = Open(a, b);
        await Compare(vm, window);
        vm.ExpandAllCommand.Execute(null);
        Pump(window);
        var column = FileColumns(vm)[0];
        var list = ViewOf(window, column).FindControl<ListBox>("KeysTree")!;
        var scroller = list.GetVisualDescendants().OfType<ScrollViewer>().First();

        var width = scroller.Extent.Width;
        Assert.True(width > scroller.Viewport.Width);
        list.ScrollIntoView(column.Rows[^1]);
        Pump(window);
        Assert.Equal(width, scroller.Extent.Width);
        window.Close();
    });

    // A file dropped before the first comparison, or a single file on the command line: "Loading…" does
    // not stay in the status bar.
    [Fact]
    public Task LoadWithoutAComparison_LeavesNoLoadingText() => Run(async () =>
    {
        var (vm, window) = Open();
        await vm.HandleDroppedPathsAsync([_baseline]);
        await Idle(vm, window);
        Assert.Single(vm.LoadedFiles);
        Assert.DoesNotContain("Loading", vm.StatusText, StringComparison.Ordinal);
        window.Close();

        var (single, singleWindow) = Open();
        await single.OpenAtStartAsync([_ws1]);
        await Idle(single, singleWindow);
        Assert.DoesNotContain("Loading", single.StatusText, StringComparison.Ordinal);
        singleWindow.Close();
    });

    // In a values grid the arrow keys move one selected row (a click toggles rows), and the other
    // columns follow it.
    [Fact]
    public Task ArrowKeys_InTheValuesGrid_MoveOneSelectedRow() => Run(async () =>
    {
        var (vm, window) = Open(_baseline, _ws1);
        await Compare(vm, window);
        var columns = FileColumns(vm);
        Select(window, columns[0], Browser, "");
        var grid = ViewOf(window, columns[0]).FindControl<ListBox>("ValuesGrid")!;
        grid.ContainerFromItem(grid.SelectedItem!)!.Focus(NavigationMethod.Directional);

        Press(window, PhysicalKey.ArrowDown);
        Assert.Single(grid.SelectedItems!);
        Assert.Equal("HomepageLocation", columns[0].SelectedValueRow!.Name);
        Assert.Equal("HomepageLocation", columns[1].SelectedValueRow!.Name);
        Press(window, PhysicalKey.ArrowDown);
        Press(window, PhysicalKey.ArrowUp);
        Assert.Single(grid.SelectedItems!);
        Assert.Equal("HomepageLocation", columns[0].SelectedValueRow!.Name);
        window.Close();
    });

    // With the selected key hidden under a closed branch, Left and Right first go to that branch.
    [Fact]
    public Task ArrowKeys_WithTheSelectedKeyHidden_GoToItsBranch() => Run(async () =>
    {
        var (vm, window) = Open(_baseline, _ws1);
        await Compare(vm, window);
        var column = FileColumns(vm)[0];
        Select(window, column, Browser, null);
        var list = ViewOf(window, column).FindControl<ListBox>("KeysTree")!;
        var contoso = column.SelectedNode!.Parent!;
        contoso.IsExpanded = false;
        Pump(window);
        list.ContainerFromItem(contoso)!.Focus(NavigationMethod.Directional);

        Press(window, PhysicalKey.ArrowLeft);
        Assert.Same(contoso, column.SelectedNode);
        Assert.Same(contoso, list.SelectedItem);
        Assert.False(contoso.IsExpanded);
        window.Close();
    });

    // A file dropped again while a comparison runs: the message stays beside the comparison's count.
    [Fact]
    public Task LoadMessage_DuringAComparison_StaysBesideItsCount() => Run(async () =>
    {
        var (vm, window) = Open(_baseline, _ws1);
        var running = vm.CompareCommand.ExecuteAsync(null);
        await vm.HandleDroppedPathsAsync([_baseline]);
        await running;
        await Idle(vm, window);
        Assert.StartsWith("Compared files = 2", vm.StatusText, StringComparison.Ordinal);
        Assert.Contains("Ignored (already added)", vm.StatusText, StringComparison.Ordinal);
        window.Close();
    });

    // Every file removed while a comparison runs: the count of an earlier comparison does not stay.
    [Fact]
    public Task RemovingEveryFileDuringAComparison_LeavesNoCount() => Run(async () =>
    {
        var (vm, window) = Open(_baseline, _ws1);
        await Compare(vm, window);
        var running = vm.CompareCommand.ExecuteAsync(null);
        vm.RemoveLoadedFile(vm.LoadedFiles[1]);
        vm.RemoveLoadedFile(vm.LoadedFiles[0]);
        await running;
        await Idle(vm, window);
        Assert.Empty(vm.LoadedFiles);
        Assert.DoesNotContain("Compared files = 2", vm.StatusText, StringComparison.Ordinal);
        window.Close();
    });

    // A message about a file is not told any more once that file is removed.
    [Fact]
    public Task RemovedFile_TakesItsMessageAway() => Run(async () =>
    {
        var empty = Write("empty3.reg", "");
        var (vm, window) = Open(_baseline, _ws1);
        await vm.HandleDroppedPathsAsync([empty]);
        await Idle(vm, window);
        Assert.Contains("No registry key in", vm.StatusText, StringComparison.Ordinal);

        vm.RemoveLoadedFile(vm.LoadedFiles.Single(f => f.File.Path == empty));
        await Compare(vm, window);
        Assert.Equal("Compared files = 2", vm.StatusText);
        window.Close();
    });
}
