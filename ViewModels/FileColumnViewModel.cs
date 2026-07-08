// Column bound to one .reg file: builds the key hierarchy, populates the values grid,
// and synchronizes its selection with the rest of the columns through the parent view model.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using RegCompare.Models;
using RegCompare.Services;

namespace RegCompare.ViewModels;

/// <summary>
/// Column tied to a single loaded .reg file. Owns its key tree, its values grid, and
/// the per-column status bar. Selection changes are pushed to <see cref="MainWindowViewModel"/>
/// (key path / value name) for cross-column synchronization.
/// </summary>
public sealed partial class FileColumnViewModel : ColumnViewModelBase
{
    private readonly DiffEngine _diff;
    private readonly Dictionary<string, KeyNodeViewModel> _nodeIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _totalKeys;
    private readonly int _totalValues;

    public override ColumnKind Kind => ColumnKind.File;

    /// <summary>Index of the bound file inside <see cref="DiffEngine.Files"/>.</summary>
    public int FileIndex { get; }

    /// <summary>The bound .reg file.</summary>
    public RegFile File { get; }

    /// <summary>Root nodes bound to the column's TreeView.</summary>
    public ObservableCollection<KeyNodeViewModel> Roots { get; } = [];

    [ObservableProperty]
    private KeyNodeViewModel? _selectedNode;

    public FileColumnViewModel(MainWindowViewModel main, DiffEngine diff, int fileIndex, bool isReference, bool showYearInDate)
        : base(main)
    {
        _diff = diff;
        FileIndex = fileIndex;
        File = diff.Files[fileIndex];
        IsReference = isReference;
        Title = (isReference ? "(REF) " : "") + File.FileName;
        var dateFormat = showYearInDate ? "dd/MM/yyyy HH:mm" : "dd/MM HH:mm";
        InfoText = $"{File.Modified.ToString(dateFormat, CultureInfo.InvariantCulture)}   -   {File.DisplaySize}";

        _totalKeys = File.Data.Count;
        foreach (var perKey in File.Data.Values) _totalValues += perKey.Count;

        Rebuild();
    }

    /// <summary>
    /// Rebuild the key tree, the value rows, and the status text. Called on construction and any time
    /// the filter checkboxes or the underlying file set change.
    /// </summary>
    public void Rebuild()
    {
        BuildTree();
        AutoExpandChanges();
        UpdateStatusFromSelection();
    }

    /// <summary>
    /// Force every visible TextBlock binding (tree node colors, value row colors) to re-evaluate
    /// against the new theme's brushes. Called after a dark/light switch.
    /// </summary>
    public void NotifyThemeChanged()
    {
        foreach (var root in Roots) NotifyNodeAndDescendants(root);

        // Re-create value rows for the currently selected key so the DataGridRow style binding
        // re-resolves the diff brushes against the new theme.
        var sel = SelectedNode;
        UpdateValueRows(sel?.Path);
    }

    private static void NotifyNodeAndDescendants(KeyNodeViewModel node)
    {
        node.RefreshThemeBindings();
        foreach (var child in node.Children) NotifyNodeAndDescendants(child);
    }

    /// <summary>
    /// Apply <see cref="MainWindowViewModel.SelectedKeyPath"/> as the selection in this column,
    /// expanding ancestor nodes as needed.
    /// </summary>
    public void ApplyExternalSelection()
    {
        var path = Main.SelectedKeyPath;
        if (string.IsNullOrEmpty(path) || !_nodeIndex.TryGetValue(path, out var node))
        {
            ClearSelectionInternal();
            return;
        }

        ExpandAncestorsOf(node);

        BeginExternalSelection();
        SuppressSelectionPropagation = true;
        try
        {
            if (SelectedNode is { } prev && !ReferenceEquals(prev, node)) prev.IsSelected = false;
            SelectedNode = node;
            node.IsSelected = true;
        }
        finally
        {
            SuppressSelectionPropagation = false;
            EndExternalSelectionDeferred();
        }

        UpdateValueRows(path);
        UpdateStatusFromSelection();
    }

    partial void OnSelectedNodeChanged(KeyNodeViewModel? value)
    {
        if (SuppressSelectionPropagation) return;
        UpdateValueRows(value?.Path);
        UpdateStatusFromSelection();
        Main.OnColumnKeySelected(this, value?.Path);
    }

    private void ClearSelectionInternal()
    {
        SuppressSelectionPropagation = true;
        try
        {
            if (SelectedNode is { } prev) prev.IsSelected = false;
            SelectedNode = null;
            ValueRows.Clear();
        }
        finally { SuppressSelectionPropagation = false; }
        UpdateStatusFromSelection();
    }

    private void BuildTree()
    {
        _nodeIndex.Clear();
        Roots.Clear();

        var hideUnchanged = Main.ShowOnlyChanges && !IsReference;

        foreach (var fullKey in _diff.SortedKeys)
        {
            var keyStatus = _diff.GetKeyStatus(fullKey, FileIndex);
            if (keyStatus == DiffStatus.Hidden) continue;
            if (hideUnchanged && !_diff.KeyOrSubtreeChanged(fullKey, FileIndex)) continue;

            var parts = fullKey.Split('\\');
            var pathSoFar = "";
            KeyNodeViewModel? parent = null;

            for (var i = 0; i < parts.Length; i++)
            {
                pathSoFar = i == 0 ? parts[0] : pathSoFar + "\\" + parts[i];

                if (!_nodeIndex.TryGetValue(pathSoFar, out var node))
                {
                    node = new KeyNodeViewModel(parts[i], pathSoFar);
                    _nodeIndex[pathSoFar] = node;
                    if (parent is null) Roots.Add(node);
                    else parent.Children.Add(node);
                }

                if (string.Equals(pathSoFar, fullKey, StringComparison.OrdinalIgnoreCase))
                {
                    DecorateLeafNode(node, fullKey, keyStatus);
                }
                parent = node;
            }
        }

        if (Main.HideEmptyAddedNodes && !IsReference) RemoveEmptyAddedNodes(Roots);
    }

    private void DecorateLeafNode(KeyNodeViewModel node, string fullKey, DiffStatus baseStatus)
    {
        node.Status = baseStatus;
        if (baseStatus is DiffStatus.Missing or DiffStatus.Added) node.IsChange = true;

        if (_diff.AllValues.TryGetValue(fullKey, out var valMap))
        {
            foreach (var valueName in valMap.Keys)
            {
                var vStatus = _diff.GetValueStatus(fullKey, valueName, FileIndex);
                if (vStatus is DiffStatus.Missing or DiffStatus.Added or DiffStatus.Different)
                {
                    node.IsChange = true;
                    if (node.Status is DiffStatus.Exists or DiffStatus.Reference)
                    {
                        node.Status = DiffStatus.Different;
                    }
                    break;
                }
            }
        }

        if (IsReference && _diff.ChangedAnywhereForKey(fullKey)) node.IsChange = true;
    }

    private static void RemoveEmptyAddedNodes(ObservableCollection<KeyNodeViewModel> roots)
    {
        for (var i = roots.Count - 1; i >= 0; i--)
        {
            var n = roots[i];
            RemoveEmptyAddedNodes(n.Children);
            if (n.Status == DiffStatus.Added && n.Children.Count == 0) roots.RemoveAt(i);
        }
    }

    private void AutoExpandChanges()
    {
        foreach (var root in Roots) ExpandIfDescendantChanged(root);
    }

    private bool ExpandIfDescendantChanged(KeyNodeViewModel node)
    {
        var anyChildChanged = false;
        foreach (var child in node.Children)
        {
            if (ExpandIfDescendantChanged(child)) anyChildChanged = true;
        }
        node.IsExpanded = anyChildChanged;
        return anyChildChanged || node.IsChange;
    }

    private void ExpandAncestorsOf(KeyNodeViewModel node)
    {
        var path = node.Path;
        while (true)
        {
            var idx = path.LastIndexOf('\\');
            if (idx < 0) break;
            path = path[..idx];
            if (_nodeIndex.TryGetValue(path, out var ancestor)) ancestor.IsExpanded = true;
        }
    }

    private void UpdateValueRows(string? keyPath)
    {
        ValueRows.Clear();
        if (string.IsNullOrEmpty(keyPath)) return;
        if (!_diff.AllValues.TryGetValue(keyPath, out var valMap)) return;

        var fileData = File.Data;
        var hasKeyHere = fileData.TryGetValue(keyPath, out var hereVals);

        // If the key itself does not exist in this file, the values grid stays empty.
        // The "missing key" state is already conveyed by the strikethrough red node in the
        // tree - listing value names from the union (with blank Type/Data) would be misleading.
        if (!hasKeyHere) return;

        foreach (var valueName in valMap.Keys.OrderBy(static n => n, StringComparer.OrdinalIgnoreCase))
        {
            var vStatus = _diff.GetValueStatus(keyPath, valueName, FileIndex);
            var hasValueHere = hasKeyHere && hereVals!.ContainsKey(valueName);

            if (!IsReference && Main.ShowOnlyChanges)
            {
                if (vStatus is DiffStatus.Identical or DiffStatus.Hidden) continue;
            }
            else if (IsReference)
            {
                if (!hasValueHere) continue;
            }
            else if (vStatus == DiffStatus.Hidden) continue;

            var displayValue = hasValueHere ? RegTypeFormatter.GetDisplayValue(hereVals![valueName]) : "";
            var typeName = hasValueHere ? RegTypeFormatter.GetTypeName(RegTypeFormatter.InferType(hereVals![valueName])) : "";

            var isChange = !IsReference && vStatus is DiffStatus.Missing or DiffStatus.Added or DiffStatus.Different;
            if (IsReference && _diff.ValueChangedAnywhere(keyPath, valueName)) isChange = true;

            ValueRows.Add(new ValueRowViewModel
            {
                Name = valueName,
                Type = typeName,
                DisplayValue = displayValue,
                RawValue = hasValueHere ? hereVals![valueName] : "",
                Status = vStatus,
                IsChange = isChange,
            });
        }
    }

    private void UpdateStatusFromSelection()
    {
        var inSelected = 0;
        if (SelectedNode is { } sn && File.Data.TryGetValue(sn.Path, out var vals)) inSelected = vals.Count;

        UpdateStatusText(_totalKeys, _totalValues, inSelected);
    }
}
