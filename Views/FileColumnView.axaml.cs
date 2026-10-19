// Code-behind for FileColumnView. Handles the right-edge column resize Thumb plus the
// tree and values-grid context menu commands (clipboard copy + .reg export), plus the
// values-grid header column resize Thumbs (drag to resize, double-click to auto-fit).

using System;
using System.Collections.Generic;
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
using Avalonia.Platform.Storage;
using RegCompare.Services;
using RegCompare.ViewModels;

namespace RegCompare.Views;

public partial class FileColumnView : UserControl
{
    private const double MinValuesColumnWidth = 40;
    private const double AutoFitPadding = 16;

    public FileColumnView()
    {
        InitializeComponent();
        // A row selected to follow another column is brought into view inside this column's tree or
        // grid only: the request stops here, so the columns area does not scroll sideways to it.
        AddHandler(RequestBringIntoViewEvent, static (_, e) => e.Handled = true);
    }

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
        var width = MeasureMaxWidth("Name", vm.ValueRows.Select(static r => r.DisplayName));
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
        => Math.Max(TextWidth.Measure(header), TextWidth.Widest(rowValues));


    // ---------- Tree context menu ----------

    private async void OnTreeCopyRowClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FileColumnViewModel vm || vm.SelectedNode is not { } node) return;
        await CopyToClipboardAsync(node.Name);
    }

    private async void OnTreeCopyRowWithChildrenClick(object? sender, RoutedEventArgs e)
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
        if (!RegFileWriter.HasSubtree(vm.File, node.Path))
        {
            vm.Main.StatusText = $"Nothing to export: {vm.File.FileName} has no key at {node.Path}";
            return;
        }
        await ExportAsync(vm, node.Path, () => RegFileWriter.BuildFromSubtree(vm.File, node.Path));
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

    private async void OnGridExportClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FileColumnViewModel vm) return;
        var keyPath = vm.SelectedNode?.Path;
        if (string.IsNullOrEmpty(keyPath)) return;

        var rows = GetSelectedValueRows();
        if (rows.Count == 0) return;

        // A red row (a value missing from this file) has nothing to write.
        var names = rows.Select(static r => r.Name)
            .Where(name => vm.File.Data.TryGetValue(keyPath, out var values) && values.ContainsKey(name))
            .ToList();
        if (names.Count == 0)
        {
            vm.Main.StatusText = $"Nothing to export: {vm.File.FileName} has none of the selected values";
            return;
        }
        await ExportAsync(vm, keyPath, () => RegFileWriter.BuildFromValues(vm.File, keyPath, names));
    }

    // ---------- Helpers ----------

    /// <summary>
    /// Build the export in the file's own format, ask where to save it, then write it. The status bar
    /// says where it went, or why it could not be written (a folder without write access, a full
    /// disk, a character the ANSI code page of a REGEDIT4 file cannot write).
    /// </summary>
    private async Task ExportAsync(FileColumnViewModel vm, string keyPath, Func<string> build)
    {
        try
        {
            // Encoded first: what cannot be written fails before the dialog.
            var bytes = RegFileWriter.Encode(vm.File, build());
            var path = await PickRegSaveFileAsync(SuggestExportName(keyPath));
            if (path is null) return;
            await File.WriteAllBytesAsync(path, bytes);
            vm.Main.StatusText = $"Exported to {path}";
        }
        catch (EncoderFallbackException ex)
        {
            vm.Main.StatusText = $"The export failed: {vm.File.FileName} is a REGEDIT4 file, and its ANSI code page cannot write '{ex.CharUnknown}'";
        }
        catch (Exception ex)
        {
            vm.Main.StatusText = $"The export failed: {ex.Message}";
        }
    }

    private List<ValueRowViewModel> GetSelectedValueRows()
    {
        var grid = this.FindControl<ListBox>("ValuesGrid");
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
