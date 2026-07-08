// Single registry-key node inside a column's TreeView.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RegCompare.Models;

namespace RegCompare.ViewModels;

/// <summary>
/// Node inside a column's key tree. Holds the segment name (last path component) and the
/// pre-computed diff status used by styling to color/bold/strike-through the row.
/// </summary>
public sealed partial class KeyNodeViewModel(string name, string path) : ObservableObject
{
    /// <summary>Last segment of the registry path (the displayed text).</summary>
    public string Name { get; } = name;

    /// <summary>Full registry path from the root (used as the sync key across columns).</summary>
    public string Path { get; } = path;

    /// <summary>Child sub-keys.</summary>
    public ObservableCollection<KeyNodeViewModel> Children { get; } = [];

    /// <summary>True when this node has at least one child sub-key.</summary>
    public bool HasChildren => Children.Count > 0;

    [ObservableProperty]
    private DiffStatus _status = DiffStatus.Hidden;

    /// <summary>True when this node should be drawn with the diff color/bold treatment.</summary>
    [ObservableProperty]
    private bool _isChange;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Convenience flag for the Strikethrough converter.</summary>
    public bool IsMissing => Status == DiffStatus.Missing;

    partial void OnStatusChanged(DiffStatus value) => OnPropertyChanged(nameof(IsMissing));

    /// <summary>Recursively walks all descendants in depth-first order.</summary>
    public IEnumerable<KeyNodeViewModel> EnumerateDescendants()
    {
        foreach (var child in Children)
        {
            yield return child;
            foreach (var grand in child.EnumerateDescendants()) yield return grand;
        }
    }

    /// <summary>
    /// Re-fire PropertyChanged for the brush-bound properties so the theme-aware converter
    /// is forced to re-resolve against the current <c>ActualThemeVariant</c>. Called after a
    /// dark/light switch (the converter snapshots a brush at evaluation time and would
    /// otherwise keep the stale brush from the previous theme).
    /// </summary>
    public void RefreshThemeBindings()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(IsChange));
        OnPropertyChanged(nameof(IsMissing));
    }
}
