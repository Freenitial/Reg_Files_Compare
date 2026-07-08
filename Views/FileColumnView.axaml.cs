// Code-behind for FileColumnView. Handles the right-edge column resize Thumb plus the
// tree and values-grid context menu commands (clipboard copy + .reg export), plus the
// values-grid header column resize Thumbs (drag to resize, double-click to auto-fit).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using RegCompare.Services;
using RegCompare.ViewModels;

namespace RegCompare.Views;

public partial class FileColumnView : UserControl
{
    private const double MinValuesColumnWidth = 40;
    private const double AutoFitPadding = 16;

    public FileColumnView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Outer right-edge Thumb: resizes the whole FileColumnView panel.</summary>
    private void OnResizeDelta(object? sender, VectorEventArgs e)
    {
        if (DataContext is not ColumnViewModelBase column) return;
        column.ColumnWidth = Math.Max(200, column.ColumnWidth + e.Vector.X);
    }

    // ---------- Values-grid header column resize ----------

    private void OnNameThumbDragDelta(object? sender, VectorEventArgs e)
    {
        if (DataContext is not FileColumnViewModel vm) return;
        var current = vm.Main.ValuesNameColumnWidth.Value;
        var next = Math.Max(MinValuesColumnWidth, current + e.Vector.X);
        vm.Main.ValuesNameColumnWidth = new GridLength(next, GridUnitType.Pixel);
    }

    private void OnTypeThumbDragDelta(object? sender, VectorEventArgs e)
    {
        if (DataContext is not FileColumnViewModel vm) return;
        var current = vm.Main.ValuesTypeColumnWidth.Value;
        var next = Math.Max(MinValuesColumnWidth, current + e.Vector.X);
        vm.Main.ValuesTypeColumnWidth = new GridLength(next, GridUnitType.Pixel);
    }

    private void OnNameThumbDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not FileColumnViewModel vm) return;
        var width = MeasureMaxWidth("Name", vm.ValueRows.Select(static r => r.Name));
        vm.Main.ValuesNameColumnWidth = new GridLength(Math.Ceiling(width) + AutoFitPadding, GridUnitType.Pixel);
        e.Handled = true;
    }

    private void OnTypeThumbDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not FileColumnViewModel vm) return;
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

    // ---------- Tree context menu ----------

    private async void OnTreeCopyRowClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FileColumnViewModel vm || vm.SelectedNode is not { } node) return;
        await CopyToClipboardAsync(node.Name);
    }

    private async void OnTreeCopyRowWithChildsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FileColumnViewModel vm || vm.SelectedNode is not { } node) return;
        var sb = new StringBuilder();
        sb.AppendLine(node.Name);
        AppendAsciiChildren(sb, node, "");
        await CopyToClipboardAsync(sb.ToString());
    }

    private async void OnTreeExportClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FileColumnViewModel vm || vm.SelectedNode is not { } node) return;
        var path = await PickRegSaveFileAsync(SuggestExportName(node.Path));
        if (path is null) return;
        var content = RegFileWriter.BuildFromSubtree(vm.File, node.Path);
        // "Windows Registry Editor Version 5.00" files are UTF-16LE; regedit can mangle
        // non-ASCII characters when importing UTF-8.
        await File.WriteAllTextAsync(path, content, Encoding.Unicode);
    }

    private static void AppendAsciiChildren(StringBuilder sb, KeyNodeViewModel node, string prefix)
    {
        for (var i = 0; i < node.Children.Count; i++)
        {
            var isLast = i == node.Children.Count - 1;
            var connector = isLast ? "└─ " : "├─ ";
            var childPrefix = isLast ? "   " : "│  ";

            sb.Append(prefix).Append(connector).AppendLine(node.Children[i].Name);
            AppendAsciiChildren(sb, node.Children[i], prefix + childPrefix);
        }
    }

    // ---------- Values grid context menu ----------

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

    private async void OnGridExportClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FileColumnViewModel vm) return;
        var keyPath = vm.SelectedNode?.Path;
        if (string.IsNullOrEmpty(keyPath)) return;

        var rows = GetSelectedValueRows();
        if (rows.Count == 0) return;

        var path = await PickRegSaveFileAsync(SuggestExportName(keyPath));
        if (path is null) return;

        var content = RegFileWriter.BuildFromValues(vm.File, keyPath, rows.Select(static r => r.Name));
        await File.WriteAllTextAsync(path, content, Encoding.Unicode);
    }

    // ---------- Helpers ----------

    private List<ValueRowViewModel> GetSelectedValueRows()
    {
        var grid = this.FindControl<ListBox>("ValuesGrid");
        if (grid is null) return [];
        return grid.SelectedItems?.OfType<ValueRowViewModel>().ToList() ?? [];
    }

    private async Task CopyToClipboardAsync(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;
        await clipboard.SetTextAsync(text);
    }

    private async Task<string?> PickRegSaveFileAsync(string suggestedName)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return null;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export to .reg file",
            SuggestedFileName = suggestedName,
            DefaultExtension = "reg",
            FileTypeChoices =
            [
                new FilePickerFileType("Registry file") { Patterns = ["*.reg"] },
            ],
        });

        return file?.TryGetLocalPath();
    }

    private static string SuggestExportName(string keyPath)
    {
        var lastSegment = keyPath.Split('\\').LastOrDefault() ?? "export";
        var safe = string.Concat(lastSegment.Select(static c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return $"{safe}.reg";
    }
}
