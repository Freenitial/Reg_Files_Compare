// Code-behind for SearchColumnView: column resize Thumb + values-grid context menu copy commands +
// values-grid header column resize Thumbs (drag and double-click auto-fit).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using RegCompare.ViewModels;

namespace RegCompare.Views;

public partial class SearchColumnView : UserControl
{
    private const double MinValuesColumnWidth = 40;
    private const double AutoFitPadding = 16;

    public SearchColumnView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnResizeDelta(object? sender, VectorEventArgs e)
    {
        if (DataContext is not ColumnViewModelBase column) return;
        column.ColumnWidth = Math.Max(200, column.ColumnWidth + e.Vector.X);
    }

    private void OnNameThumbDragDelta(object? sender, VectorEventArgs e)
    {
        if (DataContext is not SearchColumnViewModel vm) return;
        var current = vm.Main.ValuesNameColumnWidth.Value;
        var next = Math.Max(MinValuesColumnWidth, current + e.Vector.X);
        vm.Main.ValuesNameColumnWidth = new GridLength(next, GridUnitType.Pixel);
    }

    private void OnTypeThumbDragDelta(object? sender, VectorEventArgs e)
    {
        if (DataContext is not SearchColumnViewModel vm) return;
        var current = vm.Main.ValuesTypeColumnWidth.Value;
        var next = Math.Max(MinValuesColumnWidth, current + e.Vector.X);
        vm.Main.ValuesTypeColumnWidth = new GridLength(next, GridUnitType.Pixel);
    }

    private void OnNameThumbDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not SearchColumnViewModel vm) return;
        var width = MeasureMaxWidth("Name", vm.ValueRows.Select(static r => r.Name));
        vm.Main.ValuesNameColumnWidth = new GridLength(Math.Ceiling(width) + AutoFitPadding, GridUnitType.Pixel);
        e.Handled = true;
    }

    private void OnTypeThumbDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not SearchColumnViewModel vm) return;
        var width = MeasureMaxWidth("Type", vm.ValueRows.Select(static r => r.Type));
        vm.Main.ValuesTypeColumnWidth = new GridLength(Math.Ceiling(width) + AutoFitPadding, GridUnitType.Pixel);
        e.Handled = true;
    }

    private static double MeasureMaxWidth(string header, IEnumerable<string> rowValues)
    {
        var max = MeasureTextWidth(header);
        foreach (var v in rowValues)
        {
            var w = MeasureTextWidth(v);
            if (w > max) max = w;
        }
        return max;
    }

    private static double MeasureTextWidth(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            12,
            null);
        return formatted.Width;
    }

    private async void OnGridCopyRowsClick(object? sender, RoutedEventArgs e)
    {
        var rows = GetSelectedValueRows();
        if (rows.Count == 0) return;
        var text = string.Join('\n', rows.Select(r => $"{r.Name}\t{r.Type}\t{r.DisplayValue}"));
        await CopyToClipboardAsync(text);
    }

    private async void OnGridCopyNamesClick(object? sender, RoutedEventArgs e)
    {
        var rows = GetSelectedValueRows();
        if (rows.Count == 0) return;
        var text = string.Join('\n', rows.Select(static r => r.Name));
        await CopyToClipboardAsync(text);
    }

    private async void OnGridCopyDataClick(object? sender, RoutedEventArgs e)
    {
        var rows = GetSelectedValueRows();
        if (rows.Count == 0) return;
        var text = string.Join('\n', rows.Select(static r => r.DisplayValue));
        await CopyToClipboardAsync(text);
    }

    private List<ValueRowViewModel> GetSelectedValueRows()
    {
        var grid = this.FindControl<ListBox>("SearchValuesGrid");
        if (grid is null) return [];
        return grid.SelectedItems?.OfType<ValueRowViewModel>().ToList() ?? [];
    }

    private async Task CopyToClipboardAsync(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;
        await clipboard.SetTextAsync(text);
    }
}
