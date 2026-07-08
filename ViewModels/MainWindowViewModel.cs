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
public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly RegFileParser _parser;
    private readonly DragDropService _dragDrop;
    private readonly ILogger<MainWindowViewModel> _logger;
    private bool _isUpdatingList;
    private bool _comparePending;

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

        foreach (var item in picked)
        {
            var path = item.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path)) AddRegFile(path);
        }
    }

    /// <summary>Remove a single file (triggered by the per-row X button) and rebuild the comparison.</summary>
    public void RemoveLoadedFile(LoadedFileViewModel file)
    {
        _isUpdatingList = true;
        try
        {
            LoadedFiles.Remove(file);
            ResetReferenceIfNeeded();

            Columns.Clear();
            StatusText = "Compared files = 0";
            SelectedKeyPath = null;
            SelectedValueName = null;

            UpdateDisplayNames();
            if (LoadedFiles.Count(static f => f.IsChecked) >= 2) _ = Compare();
        }
        finally
        {
            _isUpdatingList = false;
        }
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
        // so the final display always reflects the latest checkbox/reference state.
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

        IsComparing = true;
        ComparisonProgress = 0.05;
        try
        {
            var refFile = ReferenceFile is not null && checkedFiles.Contains(ReferenceFile) ? ReferenceFile : checkedFiles[0];
            ReferenceFile = refFile;

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

            var existingSearches = Columns.OfType<SearchColumnViewModel>().ToList();
            Columns.Clear();
            for (var i = 0; i < regs.Count; i++)
            {
                var col = new FileColumnViewModel(this, diff, i, isReference: i == refIndex, showYearInDate: showYear);
                Columns.Add(col);
                ComparisonProgress = 0.25 + 0.70 * (i + 1) / regs.Count;
                await LetUiRenderAsync();
            }
            foreach (var s in existingSearches) Columns.Add(s);

            StatusText = $"Compared files = {checkedFiles.Count}";
            SelectedKeyPath = null;
            SelectedValueName = null;

            ComparisonProgress = 1.0;
            await Task.Delay(150);
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
        StatusText = "Compared files = 0";
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

    /// <summary>Add a single .reg file. Skips duplicates silently and re-runs Compare if a previous diff exists.</summary>
    public void AddRegFile(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (LoadedFiles.Any(f => string.Equals(f.File.Path, fullPath, StringComparison.OrdinalIgnoreCase)))
            {
                StatusText = $"Ignored (already added): {fullPath}";
                return;
            }

            var parsed = _parser.Parse(fullPath);
            LoadedFiles.Add(new LoadedFileViewModel(parsed)
            {
                CheckedToggled = OnLoadedFileCheckChanged,
                RemoveRequested = RemoveLoadedFile,
            });
            UpdateDisplayNames();
            ResetReferenceIfNeeded();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load {Path}", path);
            StatusText = $"Failed to load: {path}";
        }
    }

    /// <summary>Resolve a list of dropped paths and load each .reg file found.</summary>
    public void HandleDroppedPaths(IEnumerable<string> paths)
    {
        foreach (var p in _dragDrop.ResolveDroppedPaths(paths)) AddRegFile(p);
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

    /// <summary>Called by a column when its tree selection changed by user action.</summary>
    public void OnColumnKeySelected(ColumnViewModelBase source, string? keyPath)
    {
        SelectedKeyPath = keyPath;
        foreach (var col in Columns)
        {
            if (col is FileColumnViewModel fc && col != source) fc.ApplyExternalSelection();
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

    private void ResetReferenceIfNeeded()
    {
        if (ReferenceFile is not null && !LoadedFiles.Contains(ReferenceFile)) ReferenceFile = null;
        if (ReferenceFile is null && LoadedFiles.Count > 0) ReferenceFile = LoadedFiles[0];
    }

    partial void OnShowOnlyChangesChanged(bool value) => RebuildAllFileColumns();
    partial void OnHideEmptyAddedNodesChanged(bool value) => RebuildAllFileColumns();

    /// <summary>
    /// Re-run the comparison when the user picks a new reference while one is displayed.
    /// A reference must take part in the comparison, so an unchecked file gets checked first
    /// (which re-compares through the check-changed path).
    /// </summary>
    partial void OnReferenceFileChanged(LoadedFileViewModel? value)
    {
        if (_isUpdatingList || IsComparing || value is null) return;
        if (!value.IsChecked)
        {
            value.IsChecked = true;
            return;
        }
        if (Columns.OfType<FileColumnViewModel>().Any() && LoadedFiles.Count(static f => f.IsChecked) >= 2)
        {
            _ = Compare();
        }
    }

    private void RebuildAllFileColumns()
    {
        foreach (var col in Columns.OfType<FileColumnViewModel>()) col.Rebuild();
    }

    /// <summary>
    /// Called by the LoadedFileViewModel when its IsChecked changes. When a comparison is
    /// displayed (or running), re-compare; Compare itself clears the file columns when fewer
    /// than two files remain checked.
    /// </summary>
    public void OnLoadedFileCheckChanged()
    {
        if (_isUpdatingList) return;
        if (Columns.OfType<FileColumnViewModel>().Any() || IsComparing)
        {
            _ = Compare();
        }
    }
}
