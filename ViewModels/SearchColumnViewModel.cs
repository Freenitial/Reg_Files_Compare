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

    public override ColumnKind Kind => ColumnKind.Search;

    /// <summary>The term entered by the user (case-insensitive substring matching).</summary>
    public string Term { get; }

    private readonly IReadOnlyList<RegFile> _files;
    private readonly IReadOnlyList<int> _fileIndices;

    /// <summary>Tree roots bound to the search column's TreeView.</summary>
    public ObservableCollection<SearchResultNodeViewModel> SearchRoots { get; } = [];

    [ObservableProperty]
    private SearchResultNodeViewModel? _selectedSearchNode;

    public SearchColumnViewModel(MainWindowViewModel main, string term, IReadOnlyList<RegFile> files, IReadOnlyList<int> fileIndices)
        : base(main)
    {
        Term = term;
        Title = $"Search: {term}";
        InfoText = "";
        _files = files;
        _fileIndices = fileIndices;
        BuildResults();
    }

    private void BuildResults()
    {
        SearchResultNodeViewModel? firstChild = null;

        foreach (var idx in _fileIndices)
        {
            if (idx < 0 || idx >= _files.Count) continue;
            var file = _files[idx];

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
                    var valueMatched = valueName.Contains(Term, StringComparison.OrdinalIgnoreCase);
                    if (!valueMatched)
                    {
                        var disp = RegTypeFormatter.GetDisplayValue(raw);
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

            var fileRoot = new SearchResultNodeViewModel(file.FileName, isFileRoot: true) { FileIndex = idx };
            foreach (var (keyPath, entry) in perKey.OrderBy(static kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
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

        if (firstChild is not null)
        {
            SuppressSelectionPropagation = true;
            try { SelectedSearchNode = firstChild; firstChild.IsSelected = true; }
            finally { SuppressSelectionPropagation = false; }
            ApplySearchSelection(firstChild);
        }
    }

    partial void OnSelectedSearchNodeChanged(SearchResultNodeViewModel? value)
    {
        if (SuppressSelectionPropagation) return;
        ApplySearchSelection(value);
    }

    private void ApplySearchSelection(SearchResultNodeViewModel? node)
    {
        if (node is null || node.IsFileRoot || string.IsNullOrEmpty(node.KeyPath))
        {
            ValueRows.Clear();
            UpdateStatusFromNode(node);
            return;
        }

        if (node.FileIndex < 0 || node.FileIndex >= _files.Count) return;
        var file = _files[node.FileIndex];
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
                    DisplayValue = RegTypeFormatter.GetDisplayValue(raw),
                    RawValue = raw,
                    Status = DiffStatus.Identical,
                    IsChange = false,
                });
            }
        }

        UpdateStatusFromNode(node);

        if (!SuppressSelectionPropagation) Main.OnColumnKeySelected(this, node.KeyPath);
    }

    private void UpdateStatusFromNode(SearchResultNodeViewModel? node)
    {
        if (node is null || node.FileIndex < 0 || node.FileIndex >= _files.Count)
        {
            UpdateStatusText(0, 0, 0);
            return;
        }
        var data = _files[node.FileIndex].Data;
        var totalKeys = data.Count;
        var totalValues = 0;
        foreach (var perKey in data.Values) totalValues += perKey.Count;
        var inSelected = node.IsFileRoot ? 0 :
            (data.TryGetValue(node.KeyPath, out var vals) ? vals.Count : 0);
        UpdateStatusText(totalKeys, totalValues, inSelected);
    }
}
