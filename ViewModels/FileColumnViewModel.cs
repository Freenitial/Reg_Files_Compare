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

    /// <summary>Index of the bound file inside <see cref="DiffEngine.Files"/>.</summary>
    public int FileIndex { get; }

    /// <summary>The bound .reg file.</summary>
    public RegFile File { get; }

    /// <summary>Root nodes of the column's key tree.</summary>
    public ObservableCollection<KeyNodeViewModel> Roots { get; } = [];

    /// <summary>The visible rows of the key tree, shown by a virtualized list.</summary>
    public TreeRows<KeyNodeViewModel> Rows { get; } = new(static node => node.Name);

    [ObservableProperty]
    private KeyNodeViewModel? _selectedNode;

    /// <param name="name">The file as the title names it: its file name, with its folder when another
    /// compared file has the same name.</param>
    public FileColumnViewModel(MainWindowViewModel main, DiffEngine diff, int fileIndex, bool isReference, bool showYearInDate, string? name = null)
        : base(main)
    {
        _diff = diff;
        FileIndex = fileIndex;
        File = diff.Files[fileIndex];
        IsReference = isReference;
        Title = (isReference ? "(REF) " : "") + (name ?? File.FileName);
        HeaderToolTip = File.Path;
        var dateFormat = showYearInDate ? "dd/MM/yyyy HH:mm" : "dd/MM HH:mm";
        InfoText = $"{File.Modified.ToString(dateFormat, CultureInfo.InvariantCulture)}   -   {File.DisplaySize}";

        _totalKeys = File.Data.Count;
        foreach (var perKey in File.Data.Values) _totalValues += perKey.Count;

        BuildTree();
        AutoExpandChanges();
        Rows.SetRoots(Roots);
        UpdateStatusFromSelection();
    }

    /// <summary>Expand every key of the tree (one change for the list).</summary>
    public void ExpandAll() => SetExpandedEverywhere(true);

    /// <summary>Collapse every key of the tree (one change for the list).</summary>
    public void CollapseAll() => SetExpandedEverywhere(false);

    private void SetExpandedEverywhere(bool expanded)
    {
        using (Rows.Batch())
        {
            foreach (var root in Roots)
            {
                root.IsExpanded = expanded;
                foreach (var d in root.EnumerateDescendants()) d.IsExpanded = expanded;
            }
        }
    }

    /// <summary>
    /// Rebuild the key tree and the value rows after a filter change, then select again the key
    /// and the value selected in every column, when this column still lists them.
    /// </summary>
    public void Rebuild()
    {
        // Emptying the tree and the grid clears their selection: that must not reach the other
        // columns, whose selection is kept.
        SuppressSelectionPropagation = true;
        try
        {
            SelectedNode = null;
            ValueRows.Clear();
            BuildTree();
            AutoExpandChanges();
            Rows.SetRoots(Roots);
        }
        finally { SuppressSelectionPropagation = false; }

        ApplyExternalSelection();
        ApplyExternalValueSelection();
    }

    /// <summary>
    /// Force every visible TextBlock binding (tree node colors, value row colors) to re-evaluate
    /// against the new theme's brushes. Called after a dark/light switch.
    /// </summary>
    public void NotifyThemeChanged()
    {
        foreach (var root in Roots) NotifyNodeAndDescendants(root);
        foreach (var row in ValueRows) row.RefreshThemeBindings();
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

        SuppressSelectionPropagation = true;
        try
        {
            SelectedNode = node;
            UpdateValueRows(path);
        }
        finally { SuppressSelectionPropagation = false; }

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
            SelectedNode = null;
            ValueRows.Clear();
            UpdateDataColumnWidth();
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

    /// <summary>
    /// "Hide empty added nodes": drop the added keys that have no subkey left, then the path levels
    /// (not keys of any compared file) left without children. They leave the selection index too, so
    /// a key chosen elsewhere is not "selected" here while the tree hides it.
    /// </summary>
    private void RemoveEmptyAddedNodes(ObservableCollection<KeyNodeViewModel> nodes)
    {
        for (var i = nodes.Count - 1; i >= 0; i--)
        {
            var n = nodes[i];
            RemoveEmptyAddedNodes(n.Children);
            if (n.Children.Count == 0 && n.Status is DiffStatus.Added or DiffStatus.Hidden)
            {
                _nodeIndex.Remove(n.Path);
                nodes.RemoveAt(i);
            }
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

    /// <summary>Fill the values grid with the key's values, then size its Data column.</summary>
    private void UpdateValueRows(string? keyPath)
    {
        FillValueRows(keyPath);
        UpdateDataColumnWidth();
    }

    private void FillValueRows(string? keyPath)
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

            var displayValue = hasValueHere ? RegTypeFormatter.GetDisplayValue(hereVals![valueName], File.IsRegedit4, File.LfLines) : "";
            var typeName = hasValueHere ? RegTypeFormatter.GetTypeName(RegTypeFormatter.InferType(hereVals![valueName])) : "";

            var isChange = !IsReference && vStatus is DiffStatus.Missing or DiffStatus.Added or DiffStatus.Different;
            if (IsReference && _diff.ValueChangedAnywhere(keyPath, valueName)) isChange = true;

            ValueRows.Add(new ValueRowViewModel
            {
                Name = valueName,
                Type = typeName,
                DisplayValue = displayValue,
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
