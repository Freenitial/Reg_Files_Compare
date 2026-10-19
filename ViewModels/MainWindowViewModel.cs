// Root view model: owns the loaded files, the columns, the diff engine, and all shared selection state.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RegCompare.Models;
using RegCompare.Services;

namespace RegCompare.ViewModels;

/// <summary>
/// Top-level view model. Owns the file list, the column collection, the current
/// <see cref="DiffEngine"/>, and the cross-column synchronization state
/// (<see cref="SelectedKeyPath"/>, <see cref="SelectedValueName"/>).
/// </summary>
/// <remarks>
/// Once a comparison of two files or more has been made, the display follows the file list
/// (<see cref="_following"/>): ticking or unticking a file, choosing a reference, adding or removing
/// files compares again, until the list is emptied. A change made while a comparison runs is
/// replayed when it ends.
/// </remarks>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    /// <summary>At most this many load problems are listed in the status bar.</summary>
    private const int MaxProblemsShown = 10;

    private readonly RegFileParser _parser;
    private readonly DragDropService _dragDrop;
    private readonly ILogger<MainWindowViewModel> _logger;
    private bool _isUpdatingList;
    private bool _comparePending;

    /// <summary>
    /// The display follows the file list: set by the first comparison of two files or more, cleared
    /// when the list is emptied.
    /// </summary>
    private bool _following;

    /// <summary>Set while the application itself writes <see cref="ReferenceFile"/> (not a user's choice).</summary>
    private bool _settingReference;

    /// <summary>
    /// Problems met while loading the last files (unreadable, already added, no key). The status bar
    /// shows them, and the comparison that follows keeps them beside its own count.
    /// </summary>
    private readonly List<string> _loadProblems = [];

    public ThemeService Theme { get; }

    /// <summary>Files loaded into the top file list (checked or not).</summary>
    public ObservableCollection<LoadedFileViewModel> LoadedFiles { get; } = [];

    /// <summary>Currently rendered columns (file columns followed by zero or more search columns).</summary>
    public ObservableCollection<ColumnViewModelBase> Columns { get; } = [];

    [ObservableProperty]
    private LoadedFileViewModel? _referenceFile;

    [ObservableProperty]
    private bool _showOnlyChanges = true;

    [ObservableProperty]
    private bool _hideEmptyAddedNodes;

    [ObservableProperty]
    private string _statusText = "Compared files = 0";

    [ObservableProperty]
    private string _searchTerm = "";

    [ObservableProperty]
    private string? _selectedKeyPath;

    /// <summary>Name of the selected value (empty for the default value), null when none is selected.</summary>
    [ObservableProperty]
    private string? _selectedValueName;

    [ObservableProperty]
    private bool _isFileListVisible = true;

    [ObservableProperty]
    private bool _isDropHintVisible = true;

    /// <summary>True while a comparison is running. Drives the background fill of the file list area.</summary>
    [ObservableProperty]
    private bool _isComparing;

    /// <summary>0..1 progress of the running comparison (start ~0.05, diff engine 0.25, columns 0.25..0.95, done 1).</summary>
    [ObservableProperty]
    private double _comparisonProgress;

    /// <summary>
    /// Width of the "Name" column in every values grid. Shared across all columns so resizing
    /// from any panel updates every panel at once.
    /// </summary>
    [ObservableProperty]
    private global::Avalonia.Controls.GridLength _valuesNameColumnWidth = new(120, global::Avalonia.Controls.GridUnitType.Pixel);

    /// <summary>Width of the "Type" column in every values grid (shared).</summary>
    [ObservableProperty]
    private global::Avalonia.Controls.GridLength _valuesTypeColumnWidth = new(80, global::Avalonia.Controls.GridUnitType.Pixel);

    public MainWindowViewModel(RegFileParser parser, DragDropService dragDrop, ThemeService theme, ILogger<MainWindowViewModel> logger)
    {
        _parser = parser;
        _dragDrop = dragDrop;
        _logger = logger;
        Theme = theme;

        LoadedFiles.CollectionChanged += (_, _) => UpdateDropHint();
        UpdateDropHint();

        Theme.ThemeApplied += (_, _) =>
        {
            foreach (var col in Columns.OfType<FileColumnViewModel>()) col.NotifyThemeChanged();
        };
    }

    private void UpdateDropHint() => IsDropHintVisible = LoadedFiles.Count == 0;

    /// <summary>Browse for .reg files via the system OpenFileDialog.</summary>
    [RelayCommand]
    private async Task BrowseFilesAsync(Window? owner)
    {
        if (owner is null) return;
        var topLevel = TopLevel.GetTopLevel(owner);
        if (topLevel is null) return;

        var picked = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select .reg files to compare",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Registry files") { Patterns = ["*.reg"] },
                new FilePickerFileType("All files") { Patterns = ["*.*"] },
            ],
        });

        AddFiles(picked.Select(static item => item.TryGetLocalPath()).OfType<string>().Where(static p => p.Length > 0));
    }

    /// <summary>
    /// Remove a single file (the per-row X button, or the X of its column). When the display follows
    /// the file list, the comparison is made again without it. The search columns stay: they keep the
    /// results they were built with.
    /// </summary>
    public void RemoveLoadedFile(LoadedFileViewModel file)
    {
        _isUpdatingList = true;
        try
        {
            LoadedFiles.Remove(file);
            ResetReferenceIfNeeded();
            UpdateDisplayNames();
            if (LoadedFiles.Count == 0) _following = false;
        }
        finally
        {
            _isUpdatingList = false;
        }

        // The removed file's column goes at once; the comparison is made again (or cleared, with
        // fewer than two ticked files) when the display follows the list, or one is running, and the
        // new columns replace the others.
        var column = Columns.OfType<FileColumnViewModel>().FirstOrDefault(c => ReferenceEquals(c.File, file.File));
        if (column is not null) Columns.Remove(column);
        if (_following || IsComparing) _ = Compare();
    }

    /// <summary>
    /// Run the comparison asynchronously, surfacing progress through <see cref="IsComparing"/>
    /// and <see cref="ComparisonProgress"/>. The diff engine is built on a background thread
    /// (its construction is the heaviest part); the per-column tree builds run back on the UI
    /// thread (they create ObservableCollections bound to the TreeView) but with a Task.Yield
    /// between each so the progress bar visibly fills.
    /// </summary>
    [RelayCommand]
    private async Task Compare()
    {
        // A request that arrives while a comparison is running is replayed once it finishes,
        // so the final display always reflects the latest state of the file list.
        if (IsComparing)
        {
            _comparePending = true;
            return;
        }

        var checkedFiles = LoadedFiles.Where(static f => f.IsChecked).ToList();
        if (checkedFiles.Count < 2)
        {
            ClearFileColumns();
            return;
        }

        _following = true;
        IsComparing = true;
        ComparisonProgress = 0.05;
        try
        {
            var refFile = ReferenceFile is not null && checkedFiles.Contains(ReferenceFile) ? ReferenceFile : checkedFiles[0];
            SetReference(refFile);

            var orderedFiles = new List<LoadedFileViewModel>(checkedFiles.Count) { refFile };
            orderedFiles.AddRange(checkedFiles.Where(f => f != refFile));
            var regs = orderedFiles.Select(static f => f.File).ToList();
            var refIndex = 0;

            // Wait for one render frame so the initial 5% becomes visible before the heavy work.
            await LetUiRenderAsync();

            // Heaviest part of the comparison - run on a thread-pool worker so the UI keeps rendering.
            var diff = await Task.Run(() => new DiffEngine(regs, refIndex));
            ComparisonProgress = 0.25;
            await LetUiRenderAsync();

            var years = regs.Select(static f => f.Modified.Year).Distinct().Count();
            var showYear = years > 1;

            // The file columns are replaced in place, before the search columns, which stay as they
            // are (one opened or closed while the columns are built included).
            for (var i = Columns.Count - 1; i >= 0; i--)
            {
                if (Columns[i] is FileColumnViewModel) Columns.RemoveAt(i);
            }
            for (var i = 0; i < regs.Count; i++)
            {
                var col = new FileColumnViewModel(this, diff, i, isReference: i == refIndex, showYearInDate: showYear);
                Columns.Insert(FileColumnsEnd(), col);
                ComparisonProgress = 0.25 + 0.70 * (i + 1) / regs.Count;
                await LetUiRenderAsync();
            }

            // The selected key and value, kept from before or picked in a column already built, are
            // selected in every new column that shows them.
            foreach (var col in Columns.OfType<FileColumnViewModel>())
            {
                col.ApplyExternalSelection();
                col.ApplyExternalValueSelection();
            }

            // A replay to come shows the load problems again, beside its own count.
            StatusText = WithLoadProblems($"Compared files = {checkedFiles.Count}");
            if (!_comparePending) _loadProblems.Clear();

            ComparisonProgress = 1.0;
            await Task.Delay(150);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Comparison failed");
            ClearFileColumns();
            StatusText = $"The comparison failed: {ex.Message}";
        }
        finally
        {
            IsComparing = false;
            ComparisonProgress = 0;
            if (_comparePending)
            {
                _comparePending = false;
                _ = Compare();
            }
        }
    }

    /// <summary>Index after the last file column: where the search columns start.</summary>
    private int FileColumnsEnd()
    {
        for (var i = 0; i < Columns.Count; i++)
        {
            if (Columns[i] is not FileColumnViewModel) return i;
        }
        return Columns.Count;
    }

    /// <summary>
    /// Drop every file column (search columns are kept) and reset the shared selection state.
    /// Used when fewer than two files are checked so no stale comparison stays on screen.
    /// </summary>
    private void ClearFileColumns()
    {
        if (!Columns.OfType<FileColumnViewModel>().Any()) return;
        for (var i = Columns.Count - 1; i >= 0; i--)
        {
            if (Columns[i] is FileColumnViewModel) Columns.RemoveAt(i);
        }
        StatusText = WithLoadProblems("Compared files = 0");
        _loadProblems.Clear();
        SelectedKeyPath = null;
        SelectedValueName = null;
    }

    /// <summary>
    /// Yield until the dispatcher has actually drained the render queue. <c>Task.Yield()</c> alone
    /// is not enough: in Avalonia the awaiter resumes at <see cref="DispatcherPriority.Default"/>
    /// (8), which is higher than <see cref="DispatcherPriority.Render"/> (7), so the continuation
    /// runs *before* the next frame is painted. Posting at <see cref="DispatcherPriority.Background"/>
    /// (4) forces the dispatcher to first process every higher-priority item (Render included)
    /// before our empty callback runs - by then the new <see cref="ComparisonProgress"/> value is
    /// actually on screen.
    /// </summary>
    private static Task LetUiRenderAsync() =>
        Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background).GetTask();

    /// <summary>Expand every node in every column tree (file columns only).</summary>
    [RelayCommand]
    private void ExpandAll()
    {
        foreach (var col in Columns.OfType<FileColumnViewModel>())
        {
            foreach (var root in col.Roots)
            {
                root.IsExpanded = true;
                foreach (var d in root.EnumerateDescendants()) d.IsExpanded = true;
            }
        }
    }

    /// <summary>Collapse every node in every column tree.</summary>
    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var col in Columns.OfType<FileColumnViewModel>())
        {
            foreach (var root in col.Roots)
            {
                root.IsExpanded = false;
                foreach (var d in root.EnumerateDescendants()) d.IsExpanded = false;
            }
        }
    }

    /// <summary>Append a new search column for the term in <see cref="SearchTerm"/>.</summary>
    [RelayCommand]
    private void AddSearchColumn()
    {
        var term = (SearchTerm ?? "").Trim();
        if (term.Length == 0) return;

        var checkedFiles = LoadedFiles.Where(static f => f.IsChecked).ToList();
        var allFiles = LoadedFiles.Select(static f => f.File).ToList();
        var indices = checkedFiles.Count > 0
            ? checkedFiles.Select(f => LoadedFiles.IndexOf(f)).ToList()
            : Enumerable.Range(0, allFiles.Count).ToList();

        Columns.Add(new SearchColumnViewModel(this, term, allFiles, indices));
    }

    /// <summary>
    /// Add a single .reg file (checked), as a batch of one (see <see cref="AddFiles"/>). True when it
    /// was added.
    /// </summary>
    public bool AddRegFile(string path)
    {
        var before = LoadedFiles.Count;
        AddFiles([path]);
        return LoadedFiles.Count > before;
    }

    /// <summary>
    /// Load files (each one ticked). A file already in the list is not added twice, and one without
    /// any key is added with a word in the status bar, which lists every problem met. When the display
    /// follows the file list, the comparison is then made again once, with the new files.
    /// </summary>
    private void AddFiles(IEnumerable<string> paths, IEnumerable<string>? unreadable = null)
    {
        _loadProblems.Clear();
        if (unreadable is not null)
        {
            foreach (var path in unreadable) _loadProblems.Add($"Could not read: {path}");
        }

        var added = 0;
        foreach (var path in paths)
        {
            if (Load(path)) added++;
        }

        if (_loadProblems.Count > 0) StatusText = WithLoadProblems(null);
        if (added > 0 && _following) _ = Compare();
    }

    /// <summary>Parse one file and add it to the list; a problem goes to <see cref="_loadProblems"/>.</summary>
    private bool Load(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (LoadedFiles.Any(f => string.Equals(f.File.Path, fullPath, StringComparison.OrdinalIgnoreCase)))
            {
                _loadProblems.Add($"Ignored (already added): {fullPath}");
                return false;
            }

            var parsed = _parser.Parse(fullPath);
            if (parsed.Data.Count == 0) _loadProblems.Add($"No registry key in: {fullPath}");

            LoadedFiles.Add(new LoadedFileViewModel(parsed)
            {
                CheckedToggled = OnLoadedFileCheckChanged,
                RemoveRequested = RemoveLoadedFile,
            });
            UpdateDisplayNames();
            ResetReferenceIfNeeded();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load {Path}", path);
            _loadProblems.Add($"Failed to load {path}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// <paramref name="text"/> followed by the load problems still to tell (the first ones, when
    /// there are many), or the problems alone when <paramref name="text"/> is null.
    /// </summary>
    private string WithLoadProblems(string? text)
    {
        if (_loadProblems.Count == 0) return text ?? "";
        var shown = string.Join("   |   ", _loadProblems.Take(MaxProblemsShown));
        if (_loadProblems.Count > MaxProblemsShown) shown += $"   |   … and {_loadProblems.Count - MaxProblemsShown} more";
        return text is null ? shown : $"{text}   |   {shown}";
    }

    /// <summary>
    /// Resolve a list of dropped paths and load each .reg file found (folders are searched at every
    /// depth). Nothing found, and paths that could not be read, are told in the status bar.
    /// </summary>
    public void HandleDroppedPaths(IEnumerable<string> paths)
    {
        try
        {
            var unreadable = new List<string>();
            var files = _dragDrop.ResolveDroppedPaths(paths, unreadable);
            if (files.Count == 0 && unreadable.Count == 0)
            {
                StatusText = "No .reg file found in what was given";
                return;
            }
            AddFiles(files, unreadable);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve the dropped paths");
            StatusText = $"The files could not be listed: {ex.Message}";
        }
    }

    /// <summary>
    /// Load the files and folders given on the command line (resolved like a drop), then compare them
    /// when there are at least two.
    /// </summary>
    public void OpenAtStart(IEnumerable<string> paths)
    {
        HandleDroppedPaths(paths);
        if (LoadedFiles.Count(static f => f.IsChecked) >= 2) _ = Compare();
    }

    /// <summary>Remove a column. For file columns this also removes the file; for search columns it just drops the column.</summary>
    public void RemoveColumn(ColumnViewModelBase column)
    {
        if (column is SearchColumnViewModel)
        {
            Columns.Remove(column);
            return;
        }

        if (column is FileColumnViewModel fileCol)
        {
            var loaded = LoadedFiles.FirstOrDefault(f => ReferenceEquals(f.File, fileCol.File));
            if (loaded is not null) RemoveLoadedFile(loaded);
            else Columns.Remove(column);
        }
    }

    /// <summary>
    /// Called by a column when its tree selection changed by user action: every file column selects
    /// that key, and no value is selected any more (it belonged to the previous key).
    /// </summary>
    public void OnColumnKeySelected(ColumnViewModelBase source, string? keyPath)
    {
        SelectedKeyPath = keyPath;
        SelectedValueName = null;
        foreach (var col in Columns)
        {
            if (col == source) continue;
            if (col is FileColumnViewModel fc) fc.ApplyExternalSelection();
            else col.ApplyExternalValueSelection();
        }
    }

    /// <summary>Called by a column when its value-row selection changed by user action.</summary>
    public void OnColumnValueSelected(ColumnViewModelBase source, string? name)
    {
        SelectedValueName = name;
        foreach (var col in Columns)
        {
            if (col != source) col.ApplyExternalValueSelection();
        }
    }

    /// <summary>Compute display names: filename only when all files share the same folder, otherwise full path.</summary>
    private void UpdateDisplayNames()
    {
        var folders = LoadedFiles.Select(f => Path.GetDirectoryName(f.File.Path) ?? "").Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var fullPaths = folders > 1 && LoadedFiles.Count > 1;
        foreach (var f in LoadedFiles)
        {
            f.DisplayName = fullPaths ? f.File.Path : f.File.FileName;
        }
    }

    /// <summary>A removed reference is replaced by the first file of the list.</summary>
    private void ResetReferenceIfNeeded()
    {
        if (ReferenceFile is not null && !LoadedFiles.Contains(ReferenceFile)) SetReference(null);
        if (ReferenceFile is null && LoadedFiles.Count > 0) SetReference(LoadedFiles[0]);
    }

    /// <summary>Write <see cref="ReferenceFile"/> without taking it for a user's choice.</summary>
    private void SetReference(LoadedFileViewModel? file)
    {
        _settingReference = true;
        try { ReferenceFile = file; }
        finally { _settingReference = false; }
    }

    partial void OnShowOnlyChangesChanged(bool value) => RebuildAllFileColumns();
    partial void OnHideEmptyAddedNodesChanged(bool value) => RebuildAllFileColumns();

    /// <summary>
    /// The user picked a reference. A reference must take part in the comparison, so an unchecked
    /// file gets checked first (which compares again through the check-changed path). When the
    /// display follows the file list, the comparison is made again with it, after the one running.
    /// </summary>
    partial void OnReferenceFileChanged(LoadedFileViewModel? value)
    {
        if (_isUpdatingList || _settingReference || value is null) return;
        if (!value.IsChecked)
        {
            value.IsChecked = true;
            return;
        }
        if (_following || IsComparing) _ = Compare();
    }

    private void RebuildAllFileColumns()
    {
        foreach (var col in Columns.OfType<FileColumnViewModel>()) col.Rebuild();
    }

    /// <summary>
    /// Called by the LoadedFileViewModel when its IsChecked changes. When the display follows the
    /// file list (or a comparison runs), compare again; Compare itself clears the file columns when
    /// fewer than two files remain checked.
    /// </summary>
    public void OnLoadedFileCheckChanged()
    {
        if (_isUpdatingList) return;
        if (_following || IsComparing) _ = Compare();
    }
}
