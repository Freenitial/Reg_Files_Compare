// Code-behind for SearchColumnView: column resize Thumb + values-grid context menu copy commands +
// values-grid header column resize Thumbs (drag and double-click auto-fit).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using RegCompare.Services;
using RegCompare.ViewModels;

namespace RegCompare.Views;

public partial class SearchColumnView : UserControl
{
    private const double MinValuesColumnWidth = 40;
    private const double AutoFitPadding = 16;

    public SearchColumnView()
    {
        InitializeComponent();
        // A row selected to follow another column is brought into view inside this column's tree or
        // grid only: the request stops here, so the columns area does not scroll sideways to it.
        AddHandler(RequestBringIntoViewEvent, static (_, e) => e.Handled = true);
    }

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
        var width = MeasureMaxWidth("Name", vm.ValueRows.Select(static r => r.DisplayName));
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
        => Math.Max(TextWidth.Measure(header), TextWidth.Widest(rowValues));


    private async void OnGridCopyRowsClick(object? sender, RoutedEventArgs e)
    {
        var rows = GetSelectedValueRows();
        if (rows.Count == 0) return;
        await CopyToClipboardAsync(ValueRowsText.Rows(rows));
    }

    private async void OnGridCopyNamesClick(object? sender, RoutedEventArgs e)
    {
        var rows = GetSelectedValueRows();
        if (rows.Count == 0) return;
        await CopyToClipboardAsync(ValueRowsText.Names(rows));
    }

    private async void OnGridCopyDataClick(object? sender, RoutedEventArgs e)
    {
        var rows = GetSelectedValueRows();
        if (rows.Count == 0) return;
        await CopyToClipboardAsync(ValueRowsText.Data(rows));
    }

    private List<ValueRowViewModel> GetSelectedValueRows()
    {
        var grid = this.FindControl<ListBox>("SearchValuesGrid");
        if (grid is null) return [];
        return grid.SelectedItems?.OfType<ValueRowViewModel>().ToList() ?? [];
    }

    /// <summary>Put a text on the clipboard; when another program holds it, the status bar says so.</summary>
    private async Task CopyToClipboardAsync(string text)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null) return;
            await clipboard.SetTextAsync(text);
        }
        catch (Exception ex)
        {
            if (DataContext is ColumnViewModelBase vm) vm.Main.StatusText = $"The clipboard could not be written: {ex.Message}";
        }
    }
}
