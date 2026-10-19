// A tree shown as a flat list: the rows of the nodes whose parents are all expanded, in depth-first
// order. A virtualized ListBox then creates a container only for the rows on screen, where a TreeView
// creates one for every visible node (seconds, then minutes, for keys with thousands of subkeys).

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using RegCompare.Services;

namespace RegCompare.ViewModels;

/// <summary>A node of a tree shown through <see cref="TreeRows{T}"/>.</summary>
public interface ITreeRow<T> where T : class, ITreeRow<T>
{
    /// <summary>0 for a root, 1 for its children… (the row's indentation).</summary>
    int Depth { get; set; }

    T? Parent { get; set; }

    /// <summary>The rows this node belongs to: told when the node is expanded or collapsed.</summary>
    TreeRows<T>? Owner { get; set; }

    bool IsExpanded { get; set; }

    IList<T> Children { get; }
}

/// <summary>
/// The visible rows of a tree. A node expanded or collapsed adds or removes the rows below it in one
/// change (Avalonia handles range changes); <see cref="Batch"/> groups many expansions into one rebuild.
/// </summary>
public sealed class TreeRows<T> : ObservableCollection<T> where T : class, ITreeRow<T>
{
    /// <summary>A row's width besides its indentation and text: the item's padding, the arrow, a margin.</summary>
    private const double RowChrome = 2 + 18 + 8 + 6;

    private const double IndentWidth = 16;

    private readonly List<T> _roots = [];
    private readonly Func<T, string> _text;
    private readonly Dictionary<T, double> _textWidths = new(ReferenceEqualityComparer.Instance);
    private int _batch;
    private double _widest = -1;

    /// <param name="text">The text a row shows (for <see cref="Widest"/>).</param>
    public TreeRows(Func<T, string> text) => _text = text;

    /// <summary>
    /// The width the widest row takes (indentation, arrow, text). The list is kept that wide whatever
    /// rows are on screen, so its horizontal scrolling does not change while it scrolls up and down.
    /// Measured when first asked for (UI thread), then kept up to date.
    /// </summary>
    public double Widest
    {
        get
        {
            if (_widest < 0)
            {
                _widest = 0;
                foreach (var row in Items) _widest = Math.Max(_widest, RowWidth(row));
            }
            return _widest;
        }
    }

    private double RowWidth(T row)
    {
        if (!_textWidths.TryGetValue(row, out var text)) _textWidths[row] = text = TextWidth.Estimate(_text(row));
        return RowChrome + row.Depth * IndentWidth + text;
    }

    /// <summary>The rows changed: <see cref="Widest"/> is measured again when next asked for.</summary>
    private void RemeasureWidest()
    {
        if (_widest < 0) return;
        _widest = -1;
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Widest)));
    }

    /// <summary>The roots of the tree.</summary>
    public IReadOnlyList<T> Roots => _roots;

    /// <summary>Show the tree under <paramref name="roots"/> (depths, parents and owner set here).</summary>
    public void SetRoots(IEnumerable<T> roots)
    {
        _roots.Clear();
        _textWidths.Clear();
        _roots.AddRange(roots);
        foreach (var root in _roots) Attach(root, 0, null);
        Rebuild();
    }

    private void Attach(T node, int depth, T? parent)
    {
        node.Depth = depth;
        node.Parent = parent;
        node.Owner = this;
        foreach (var child in node.Children) Attach(child, depth + 1, node);
    }

    /// <summary>Expand or collapse many nodes, then show the result in one change (when disposed).</summary>
    public IDisposable Batch()
    {
        _batch++;
        return new BatchScope(this);
    }

    private sealed class BatchScope(TreeRows<T> rows) : IDisposable
    {
        private TreeRows<T>? _rows = rows;

        public void Dispose()
        {
            if (_rows is null) return;
            if (--_rows._batch == 0) _rows.Rebuild();
            _rows = null;
        }
    }

    /// <summary>List the visible rows again, from the roots.</summary>
    public void Rebuild()
    {
        var rows = new List<T>(Math.Max(16, Count));
        foreach (var root in _roots) AddVisible(root, rows);
        CheckReentrancy();
        var items = (List<T>)Items;
        items.Clear();
        items.AddRange(rows);
        RaiseCountChanged();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        RemeasureWidest();
    }

    private static void AddVisible(T node, List<T> rows)
    {
        rows.Add(node);
        if (!node.IsExpanded) return;
        foreach (var child in node.Children) AddVisible(child, rows);
    }

    /// <summary>A node was expanded or collapsed: show or hide the rows below it.</summary>
    public void OnExpandedChanged(T node)
    {
        if (_batch > 0) return;
        var index = IndexOf(node);
        // Under a collapsed parent: nothing shows yet; its state counts when the parent opens.
        if (index < 0) return;

        var items = (List<T>)Items;
        if (node.IsExpanded)
        {
            var added = new List<T>();
            foreach (var child in node.Children) AddVisible(child, added);
            if (added.Count == 0) return;
            CheckReentrancy();
            items.InsertRange(index + 1, added);
            RaiseCountChanged();
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, added, index + 1));
            if (_widest >= 0)
            {
                var widest = _widest;
                foreach (var row in added) widest = Math.Max(widest, RowWidth(row));
                if (widest > _widest)
                {
                    _widest = widest;
                    OnPropertyChanged(new PropertyChangedEventArgs(nameof(Widest)));
                }
            }
        }
        else
        {
            var end = index + 1;
            while (end < items.Count && items[end].Depth > node.Depth) end++;
            var count = end - index - 1;
            if (count == 0) return;
            CheckReentrancy();
            var removed = items.GetRange(index + 1, count);
            items.RemoveRange(index + 1, count);
            RaiseCountChanged();
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, removed, index + 1));
            // Only when the widest row went with them.
            if (_widest >= 0 && removed.Exists(row => RowWidth(row) >= _widest)) RemeasureWidest();
        }
    }

    private void RaiseCountChanged()
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
    }
}
