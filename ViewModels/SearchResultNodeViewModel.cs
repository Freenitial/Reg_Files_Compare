// Tree node for the search-results column. Two kinds: file roots and matching-key children.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RegCompare.ViewModels;

/// <summary>
/// Node inside a search-result column tree. File roots group their matching key nodes.
/// </summary>
public sealed partial class SearchResultNodeViewModel(string displayText, bool isFileRoot) : ObservableObject, ITreeRow<SearchResultNodeViewModel>
{
    public string DisplayText { get; } = displayText;

    /// <summary>Tooltip: the full path of a file group's file.</summary>
    public string? ToolTip { get; init; }

    /// <summary>True when this node is a file group; false when it is a single matching key.</summary>
    public bool IsFileRoot { get; } = isFileRoot;

    public ObservableCollection<SearchResultNodeViewModel> Children { get; } = [];

    IList<SearchResultNodeViewModel> ITreeRow<SearchResultNodeViewModel>.Children => Children;

    public bool HasChildren => Children.Count > 0;

    [ObservableProperty]
    private bool _isExpanded = true;

    partial void OnIsExpandedChanged(bool value) => Owner?.OnExpandedChanged(this);

    /// <summary>Depth in the tree (0 for a file group): the row's indentation.</summary>
    public int Depth
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged(nameof(Indent));
        }
    }

    /// <summary>Left margin of the row: one step per level.</summary>
    public Thickness Indent => new(Depth * 16, 0, 0, 0);

    public SearchResultNodeViewModel? Parent { get; set; }

    public TreeRows<SearchResultNodeViewModel>? Owner { get; set; }

    /// <summary>Index of the file this node belongs to (file root or key match).</summary>
    public int FileIndex { get; init; } = -1;

    /// <summary>Full key path for key-match nodes; empty for file roots.</summary>
    public string KeyPath { get; init; } = "";

    /// <summary>Names of the values that matched the search term (used to filter the values grid).</summary>
    public IReadOnlyList<string> MatchedValueNames { get; init; } = [];

    /// <summary>True when the key path itself matched (vs. a value-only match).</summary>
    public bool KeyMatched { get; init; }
}
