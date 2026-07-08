// Thin wrapper around Avalonia's ThemeVariant switching, exposed as an INotifyPropertyChanged-compatible service.

using System;
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RegCompare.Services;

/// <summary>
/// Centralized switch between the dark and light Fluent themes.
/// The application starts in dark mode.
/// </summary>
public sealed partial class ThemeService : ObservableObject
{
    [ObservableProperty]
    private bool _isDark = true;

    /// <summary>Raised after <see cref="Apply"/> has switched the application theme.</summary>
    public event EventHandler? ThemeApplied;

    /// <summary>
    /// Apply the currently selected theme to the running Avalonia <see cref="Application"/>.
    /// Safe to call repeatedly.
    /// </summary>
    public void Apply()
    {
        var app = Application.Current;
        if (app is null) return;
        app.RequestedThemeVariant = IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        ThemeApplied?.Invoke(this, EventArgs.Empty);
    }

    partial void OnIsDarkChanged(bool value) => Apply();
}
