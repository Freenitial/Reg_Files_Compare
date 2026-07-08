// Tree node for the search-results column. Two kinds: file roots and matching-key children.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RegCompare.ViewModels;

/// <summary>
/// Node inside a search-result column tree. File roots group their matching key nodes.
/// </summary>
public sealed partial class SearchResultNodeViewModel(string displayText, bool isFileRoot) : ObservableObject
{
    public string DisplayText { get; } = displayText;

    /// <summary>True when this node is a file group; false when it is a single matching key.</summary>
    public bool IsFileRoot { get; } = isFileRoot;

    public ObservableCollection<SearchResultNodeViewModel> Children { get; } = [];

    public bool HasChildren => Children.Count > 0;

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Index of the file this node belongs to (file root or key match).</summary>
    public int FileIndex { get; init; } = -1;

    /// <summary>Full key path for key-match nodes; empty for file roots.</summary>
    public string KeyPath { get; init; } = "";

    /// <summary>Names of the values that matched the search term (used to filter the values grid).</summary>
    public IReadOnlyList<string> MatchedValueNames { get; init; } = [];

    /// <summary>True when the key path itself matched (vs. a value-only match).</summary>
    public bool KeyMatched { get; init; }
}
