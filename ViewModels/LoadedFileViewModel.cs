// One row in the top "loaded files" list.
// IsChecked changes trigger a re-comparison via MainWindowViewModel.
// The Remove command, bound to the per-row X button, deletes the file from the list.

using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RegCompare.Models;

namespace RegCompare.ViewModels;

/// <summary>
/// One loaded .reg file as displayed in the top file list.
/// </summary>
public sealed partial class LoadedFileViewModel : ViewModelBase
{
    public RegFile File { get; }

    /// <summary>Callback invoked when the user toggles the checkbox.</summary>
    public Action? CheckedToggled { get; init; }

    /// <summary>Callback invoked when the user clicks the per-row X (remove) button.</summary>
    public Action<LoadedFileViewModel>? RemoveRequested { get; init; }

    [ObservableProperty]
    private bool _isChecked = true;

    [ObservableProperty]
    private string _displayName;

    public LoadedFileViewModel(RegFile file)
    {
        File = file;
        _displayName = file.FileName;
    }

    partial void OnIsCheckedChanged(bool value) => CheckedToggled?.Invoke();

    [RelayCommand]
    private void Remove() => RemoveRequested?.Invoke(this);
}
