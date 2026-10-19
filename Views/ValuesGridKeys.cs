// The arrow keys in a values grid: one row selected at a time.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace RegCompare.Views;

/// <summary>
/// Up / Down / Home / End / Page Up / Page Down select a single row of a values grid and give it the
/// focus, so the other columns follow it. The grid's Toggle mode (a click selects or unselects a row)
/// would otherwise add or remove rows as the keys move. With Shift or Ctrl the grid's own handling stays.
/// </summary>
internal static class ValuesGridKeys
{
    public static void Attach(ListBox grid)
        => grid.AddHandler(InputElement.KeyDownEvent, (_, e) => OnKeyDown(grid, e), RoutingStrategies.Tunnel);

    private static void OnKeyDown(ListBox grid, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None || grid.ItemCount == 0) return;
        if (e.Key is not (Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown)) return;

        var current = e.Source is Visual source && source.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { } item
            ? grid.IndexFromContainer(item)
            : grid.SelectedIndex;
        var rowHeight = grid.ContainerFromIndex(Math.Max(0, current))?.Bounds.Height is > 0 and var h ? h : 20;
        var viewport = grid.Scroll?.Viewport.Height ?? grid.Bounds.Height;
        var page = Math.Max(1, (int)(viewport / rowHeight) - 1);
        var target = e.Key switch
        {
            Key.Up => current - 1,
            Key.Down => current + 1,
            Key.Home => 0,
            Key.End => grid.ItemCount - 1,
            Key.PageUp => current - page,
            _ => current + page,
        };
        target = Math.Clamp(target, 0, grid.ItemCount - 1);

        // Setting the index clears the other selected rows.
        grid.SelectedIndex = target;
        grid.ScrollIntoView(target);
        grid.ContainerFromIndex(target)?.Focus(NavigationMethod.Directional);
        e.Handled = true;
    }
}
