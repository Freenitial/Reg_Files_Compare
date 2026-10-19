// Common state shared by file columns and search-result columns.

using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RegCompare.Services;

namespace RegCompare.ViewModels;

/// <summary>
/// Base class for a single rendered column. Holds the visible header information,
/// the per-column status text, and the user-controlled width.
/// </summary>
public abstract partial class ColumnViewModelBase(MainWindowViewModel main) : ViewModelBase
{
    /// <summary>
    /// Parent <see cref="MainWindowViewModel"/>. Public so XAML bindings inside the values-grid
    /// item template can reach shared state (e.g. <see cref="MainWindowViewModel.ValuesNameColumnWidth"/>).
    /// </summary>
    public MainWindowViewModel Main { get; } = main;

    /// <summary>
    /// Gate flag set while the column applies external selection; prevents the cross-column
    /// notification from being re-emitted and looping.
    /// </summary>
    protected bool SuppressSelectionPropagation { get; set; }

    [ObservableProperty]
    private string _title = "";

    /// <summary>Tooltip of the column header: the full path of a file column's file.</summary>
    public string? HeaderToolTip { get; protected init; }

    [ObservableProperty]
    private string _infoText = "";

    [ObservableProperty]
    private string _statusText = "Keys: 0   |   Values: 0   |   Values in selected key: 0";

    [ObservableProperty]
    private bool _isReference;

    [ObservableProperty]
    private double _columnWidth = 350;

    /// <summary>Selected value row; setting it from the view triggers cross-column sync.</summary>
    [ObservableProperty]
    private ValueRowViewModel? _selectedValueRow;

    /// <summary>The rows of the values grid (a ListBox) in the lower half of the column.</summary>
    public ObservableCollection<ValueRowViewModel> ValueRows { get; } = [];

    /// <summary>Room around a datum in its cell (the cell's padding, and a little slack).</summary>
    private const double DataCellPadding = 14;

    /// <summary>
    /// Width of the values grid's Data column: its widest datum. The grid only creates the rows on
    /// screen (a key may hold thousands of values), so it cannot size that column from them all.
    /// </summary>
    [ObservableProperty]
    private GridLength _dataColumnWidth = new(0, GridUnitType.Pixel);

    /// <summary>Size the Data column to the widest datum of <see cref="ValueRows"/>, once they are filled.</summary>
    protected void UpdateDataColumnWidth()
    {
        var widest = TextWidth.Widest(ValueRows.Select(static row => row.ShownValue));
        DataColumnWidth = new GridLength(Math.Ceiling(widest) + DataCellPadding, GridUnitType.Pixel);
    }

    /// <summary>Apply <see cref="MainWindowViewModel.SelectedValueName"/> to the value row selection.</summary>
    public virtual void ApplyExternalValueSelection()
    {
        var name = Main.SelectedValueName;
        ValueRowViewModel? match = null;
        // The default value's name is empty: only null means "no value selected".
        if (name is not null)
        {
            foreach (var row in ValueRows)
            {
                if (string.Equals(row.Name, name, System.StringComparison.OrdinalIgnoreCase))
                {
                    match = row;
                    break;
                }
            }
        }

        SuppressSelectionPropagation = true;
        try { SelectedValueRow = match; }
        finally { SuppressSelectionPropagation = false; }
    }

    partial void OnSelectedValueRowChanged(ValueRowViewModel? value)
    {
        if (SuppressSelectionPropagation) return;
        OnUserSelectedValue(value);
    }

    /// <summary>The user selected a value row (or none) in this column: every other column follows.</summary>
    protected virtual void OnUserSelectedValue(ValueRowViewModel? value) => Main.OnColumnValueSelected(this, value?.Name);

    /// <summary>Remove this column (and, for file columns, its underlying file as well).</summary>
    [RelayCommand]
    private void RemoveColumn() => Main.RemoveColumn(this);

    /// <summary>Update <see cref="StatusText"/> from current key/value counts.</summary>
    protected void UpdateStatusText(int totalKeys, int totalValues, int valuesInSelectedKey)
    {
        StatusText = $"Keys: {totalKeys}   |   Values: {totalValues}   |   Values in selected key: {valuesInSelectedKey}";
    }
}
