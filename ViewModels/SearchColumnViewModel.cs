// Column showing the matches of a search term across the currently checked files.
// Selecting a matching key navigates every file column to that key.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using RegCompare.Models;
using RegCompare.Services;

namespace RegCompare.ViewModels;

/// <summary>
/// Search-result column. Built once with the term and the snapshot of currently checked files;
/// it does not refresh on filter or theme changes.
/// </summary>
public sealed partial class SearchColumnViewModel : ColumnViewModelBase
{

    /// <summary>The term entered by the user (case-insensitive substring matching).</summary>
    public string Term { get; }

    /// <summary>The files with results, by index: a file removed meanwhile is not kept in memory for
    /// nothing by an old search.</summary>
    private readonly Dictionary<int, RegFile> _files = [];

    /// <summary>Roots of the results tree: one group per file.</summary>
    public ObservableCollection<SearchResultNodeViewModel> SearchRoots { get; } = [];

    /// <summary>The visible rows of the results tree, shown by a virtualized list.</summary>
    public TreeRows<SearchResultNodeViewModel> Rows { get; } = new(static node => node.DisplayText);

    [ObservableProperty]
    private SearchResultNodeViewModel? _selectedSearchNode;

    public SearchColumnViewModel(MainWindowViewModel main, string term, IReadOnlyList<RegFile> files, IReadOnlyList<int> fileIndices)
        : base(main)
    {
        Term = term;
        Title = $"Search: {term}";
        InfoText = "";
        BuildResults(files, fileIndices);
    }

    private void BuildResults(IReadOnlyList<RegFile> files, IReadOnlyList<int> fileIndices)
    {
        SearchResultNodeViewModel? firstChild = null;
        var keys = 0;
        // Files of the same name: their folder tells them apart, as in the reference list.
        var groupNames = FileNames.Distinct(files.Select(static f => f.Path).ToList());

        foreach (var idx in fileIndices)
        {
            if (idx < 0 || idx >= files.Count) continue;
            var file = files[idx];

            var perKey = new Dictionary<string, (bool keyMatched, HashSet<string> values)>(StringComparer.OrdinalIgnoreCase);

            foreach (var (keyPath, values) in file.Data)
            {
                var keyMatched = keyPath.Contains(Term, StringComparison.OrdinalIgnoreCase);
                if (keyMatched)
                {
                    if (!perKey.TryGetValue(keyPath, out var entry))
                    {
                        perKey[keyPath] = (true, []);
                    }
                    else
                    {
                        perKey[keyPath] = (true, entry.values);
                    }
                }

                foreach (var (valueName, raw) in values)
                {
                    // The name and the data as the grids show them ("(Default)", decoded hex).
                    var valueMatched = ValueRowViewModel.DisplayNameOf(valueName).Contains(Term, StringComparison.OrdinalIgnoreCase);
                    if (!valueMatched)
                    {
                        var disp = RegTypeFormatter.GetDisplayValue(raw, file.IsRegedit4, file.LfLines);
                        valueMatched = disp.Contains(Term, StringComparison.OrdinalIgnoreCase);
                    }
                    if (valueMatched)
                    {
                        if (!perKey.TryGetValue(keyPath, out var entry))
                        {
                            entry = (false, []);
                            perKey[keyPath] = entry;
                        }
                        entry.values.Add(valueName);
                        perKey[keyPath] = entry;
                    }
                }
            }

            if (perKey.Count == 0) continue;
            _files[idx] = file;
            keys += perKey.Count;

            var fileRoot = new SearchResultNodeViewModel(groupNames[idx], isFileRoot: true) { FileIndex = idx, ToolTip = file.Path };
            foreach (var (keyPath, entry) in perKey.OrderBy(static kvp => kvp.Key, KeyPathComparer.Instance))
            {
                var keyNode = new SearchResultNodeViewModel(keyPath, isFileRoot: false)
                {
                    FileIndex = idx,
                    KeyPath = keyPath,
                    KeyMatched = entry.keyMatched,
                    MatchedValueNames = [.. entry.values.OrderBy(static n => n, StringComparer.OrdinalIgnoreCase)],
                };
                fileRoot.Children.Add(keyNode);
                firstChild ??= keyNode;
            }
            SearchRoots.Add(fileRoot);
        }

        if (SearchRoots.Count == 0)
        {
            SearchRoots.Add(new SearchResultNodeViewModel("(no results)", isFileRoot: false));
        }
        InfoText = keys == 0 ? "No match"
            : $"{keys} key{(keys == 1 ? "" : "s")} in {SearchRoots.Count} file{(SearchRoots.Count == 1 ? "" : "s")}";
        Rows.SetRoots(SearchRoots);

        if (firstChild is not null)
        {
            SuppressSelectionPropagation = true;
            try { SelectedSearchNode = firstChild; }
            finally { SuppressSelectionPropagation = false; }
            ApplySearchSelection(firstChild);
        }
    }

    partial void OnSelectedSearchNodeChanged(SearchResultNodeViewModel? value)
    {
        if (SuppressSelectionPropagation) return;
        ApplySearchSelection(value);
    }

    /// <summary>True when this column shows the values of the key selected in every column.</summary>
    private bool ShowsSelectedKey =>
        SelectedSearchNode is { IsFileRoot: false } node
        && string.Equals(node.KeyPath, Main.SelectedKeyPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The value selected elsewhere belongs to the key selected elsewhere: this grid follows it only
    /// while it shows that key, and selects nothing otherwise.
    /// </summary>
    public override void ApplyExternalValueSelection()
    {
        if (ShowsSelectedKey)
        {
            base.ApplyExternalValueSelection();
            return;
        }
        SuppressSelectionPropagation = true;
        try { SelectedValueRow = null; }
        finally { SuppressSelectionPropagation = false; }
    }

    /// <summary>
    /// A value picked here belongs to this column's key: when another key is selected elsewhere, the
    /// columns go to this one first, then select the value.
    /// </summary>
    protected override void OnUserSelectedValue(ValueRowViewModel? value)
    {
        if (value is not null && !ShowsSelectedKey && SelectedSearchNode is { IsFileRoot: false } node)
            Main.OnColumnKeySelected(this, node.KeyPath);
        base.OnUserSelectedValue(value);
    }

    private void ApplySearchSelection(SearchResultNodeViewModel? node)
    {
        if (node is null || node.IsFileRoot || string.IsNullOrEmpty(node.KeyPath))
        {
            // Emptying the grid clears its selected value: the other columns keep theirs.
            SuppressSelectionPropagation = true;
            try { ValueRows.Clear(); }
            finally { SuppressSelectionPropagation = false; }
            UpdateDataColumnWidth();
            UpdateStatusFromNode(node);
            return;
        }

        if (!_files.TryGetValue(node.FileIndex, out var file)) return;
        var nameSet = node.MatchedValueNames.Count > 0
            ? new HashSet<string>(node.MatchedValueNames, StringComparer.OrdinalIgnoreCase)
            : null;

        ValueRows.Clear();
        if (file.Data.TryGetValue(node.KeyPath, out var values))
        {
            foreach (var (valueName, raw) in values.OrderBy(static kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (nameSet is not null && !nameSet.Contains(valueName)) continue;
                ValueRows.Add(new ValueRowViewModel
                {
                    Name = valueName,
                    Type = RegTypeFormatter.GetTypeName(RegTypeFormatter.InferType(raw)),
                    DisplayValue = RegTypeFormatter.GetDisplayValue(raw, file.IsRegedit4, file.LfLines),
                    Status = DiffStatus.Identical,
                    IsChange = false,
                });
            }
        }

        UpdateDataColumnWidth();
        UpdateStatusFromNode(node);

        if (!SuppressSelectionPropagation) Main.OnColumnKeySelected(this, node.KeyPath);
    }

    private void UpdateStatusFromNode(SearchResultNodeViewModel? node)
    {
        if (node is null || !_files.TryGetValue(node.FileIndex, out var file))
        {
            UpdateStatusText(0, 0, 0);
            return;
        }
        var data = file.Data;
        var totalKeys = data.Count;
        var totalValues = 0;
        foreach (var perKey in data.Values) totalValues += perKey.Count;
        var inSelected = node.IsFileRoot ? 0 :
            (data.TryGetValue(node.KeyPath, out var vals) ? vals.Count : 0);
        UpdateStatusText(totalKeys, totalValues, inSelected);
    }
}
