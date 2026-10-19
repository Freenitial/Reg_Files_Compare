// Keeps the selected row of a tree list (a ListBox over TreeRows) and the column's selected node in step.

using System;
using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RegCompare.ViewModels;

namespace RegCompare.Views;

/// <summary>
/// The column's view model owns the selected node; the list only shows it. A row the user picks (click,
/// arrow keys) becomes the selected node; a node selected by the view model is selected in the list when
/// its row is shown, and stays selected (hidden) while a collapsed parent hides its row, as in a tree.
/// Right / Left open and close a node (or go to its first child / its parent), a double-click toggles it.
/// </summary>
internal sealed class TreeListSelection<T> where T : class, ITreeRow<T>
{
    private readonly ListBox _list;
    private readonly string _propertyName;
    private readonly Func<object?, T?> _get;
    private readonly Action<object, T> _set;
    private object? _vm;
    private INotifyCollectionChanged? _rows;
    private bool _syncing;
    private bool _syncPosted;

    public TreeListSelection(ListBox list, string propertyName, Func<object?, T?> get, Action<object, T> set)
    {
        _list = list;
        _propertyName = propertyName;
        _get = get;
        _set = set;
        list.SelectionChanged += OnSelectionChanged;
        // Before the list: its own key handling takes Right and Left.
        list.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        list.DoubleTapped += OnDoubleTapped;
    }

    /// <summary>Follow the view model given as the view's DataContext (null to let go of the last one).</summary>
    public void Attach(object? vm, INotifyCollectionChanged? rows)
    {
        if (_vm is INotifyPropertyChanged oldVm) oldVm.PropertyChanged -= OnVmPropertyChanged;
        if (_rows is not null) _rows.CollectionChanged -= OnRowsChanged;
        _vm = vm;
        _rows = rows;
        if (_vm is INotifyPropertyChanged newVm) newVm.PropertyChanged += OnVmPropertyChanged;
        if (_rows is not null) _rows.CollectionChanged += OnRowsChanged;
        Sync();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == _propertyName) Sync();
    }

    // Rows shown or hidden: the selected node may have come back into view. After the list has taken
    // the change in.
    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e) => PostSync();

    private void PostSync()
    {
        if (_syncPosted) return;
        _syncPosted = true;
        Dispatcher.UIThread.Post(() =>
        {
            _syncPosted = false;
            Sync();
        }, DispatcherPriority.Background);
    }

    /// <summary>Select the view model's node in the list, or nothing when its row is hidden.</summary>
    private void Sync()
    {
        var node = _get(_vm);
        var shown = node is not null && _list.ItemsSource is IList rows && rows.Contains(node) ? node : null;
        if (ReferenceEquals(_list.SelectedItem, shown)) return;
        _syncing = true;
        try { _list.SelectedItem = shown; }
        finally { _syncing = false; }
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _vm is null) return;
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is T node)
        {
            _set(_vm, node);
            return;
        }
        // Rows removed under a collapsed parent clear the list's selection, not the column's; a
        // Ctrl+click on the selected row neither: it is selected again, as in a tree.
        if (_list.SelectedItem is null) PostSync();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null || _get(_vm) is not { } node || e.KeyModifiers != KeyModifiers.None) return;
        if (e.Key is Key.Left or Key.Right && _list.ItemsSource is IList rows && !rows.Contains(node))
        {
            // The selected node is hidden under a closed branch: the arrows go to that branch first,
            // rather than act on rows nobody sees.
            for (var up = node.Parent; up is not null; up = up.Parent)
            {
                if (!rows.Contains(up)) continue;
                MoveTo(up);
                break;
            }
            e.Handled = true;
            return;
        }
        switch (e.Key)
        {
            case Key.Right when node.Children.Count > 0:
                if (!node.IsExpanded) node.IsExpanded = true;
                else MoveTo(node.Children[0]);
                e.Handled = true;
                break;
            case Key.Left:
                if (node.IsExpanded && node.Children.Count > 0) node.IsExpanded = false;
                else if (node.Parent is { } parent) MoveTo(parent);
                e.Handled = true;
                break;
        }
    }

    /// <summary>Select <paramref name="node"/>, show its row and give it the focus (Up / Down go on from there).</summary>
    private void MoveTo(T node)
    {
        _set(_vm!, node);
        _list.ScrollIntoView(node);
        _list.ContainerFromItem(node)?.Focus(NavigationMethod.Directional);
    }

    // A double-click on a row opens or closes it (one on its arrow already toggled it).
    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is not Avalonia.Visual source || source.FindAncestorOfType<ToggleButton>(includeSelf: true) is not null) return;
        if (source.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext is T { Children.Count: > 0 } node)
            node.IsExpanded = !node.IsExpanded;
    }
}
