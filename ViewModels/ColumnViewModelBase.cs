// Common state shared by file columns and search-result columns.

using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RegCompare.Models;

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

    /// <summary>
    /// True while a cross-column selection sync is being applied to this column. The View
    /// reads this flag and marks <c>RequestBringIntoView</c> events as Handled while it's set,
    /// so the outer horizontal ScrollViewer of the columns area does not auto-scroll to the
    /// synced column (which would shift the user's view sideways every time they click a node).
    /// </summary>
    public bool IsApplyingExternalSelection { get; private set; }

    /// <summary>Set <see cref="IsApplyingExternalSelection"/> = true (start of an external sync).</summary>
    protected void BeginExternalSelection() => IsApplyingExternalSelection = true;

    /// <summary>
    /// Reset <see cref="IsApplyingExternalSelection"/> on the next dispatcher tick so that
    /// the synchronous chain of <c>RequestBringIntoView</c> events emitted by setting
    /// SelectedItem still see the flag as true while bubbling.
    /// </summary>
    protected void EndExternalSelectionDeferred() =>
        Dispatcher.UIThread.Post(() => IsApplyingExternalSelection = false, DispatcherPriority.Background);

    /// <summary>Column kind discriminator used by the view to pick a template.</summary>
    public abstract ColumnKind Kind { get; }

    [ObservableProperty]
    private string _title = "";

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

    /// <summary>The flat collection bound to the values DataGrid in the lower half of the column.</summary>
    public ObservableCollection<ValueRowViewModel> ValueRows { get; } = [];

    /// <summary>Apply <see cref="MainWindowViewModel.SelectedValueName"/> to the value row selection.</summary>
    public virtual void ApplyExternalValueSelection()
    {
        var name = Main.SelectedValueName;
        ValueRowViewModel? match = null;
        if (!string.IsNullOrEmpty(name))
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

        BeginExternalSelection();
        SuppressSelectionPropagation = true;
        try { SelectedValueRow = match; }
        finally
        {
            SuppressSelectionPropagation = false;
            EndExternalSelectionDeferred();
        }
    }

    partial void OnSelectedValueRowChanged(ValueRowViewModel? value)
    {
        if (SuppressSelectionPropagation) return;
        Main.OnColumnValueSelected(this, value?.Name);
    }

    /// <summary>Remove this column (and, for file columns, its underlying file as well).</summary>
    [RelayCommand]
    private void RemoveColumn() => Main.RemoveColumn(this);

    /// <summary>Update <see cref="StatusText"/> from current key/value counts.</summary>
    protected void UpdateStatusText(int totalKeys, int totalValues, int valuesInSelectedKey)
    {
        StatusText = $"Keys: {totalKeys}   |   Values: {totalValues}   |   Values in selected key: {valuesInSelectedKey}";
    }
}
