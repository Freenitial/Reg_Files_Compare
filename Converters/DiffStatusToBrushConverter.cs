// Maps a DiffStatus enum to one of the diff brushes defined in the application palette.
// Returns AvaloniaProperty.UnsetValue (so the default Foreground wins) for non-diff statuses.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using RegCompare.Models;

namespace RegCompare.Converters;

/// <summary>
/// Look up a diff brush in the active theme dictionary by status. Used to color value rows.
/// </summary>
public sealed class DiffStatusToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var app = Application.Current;
        if (app is null) return AvaloniaProperty.UnsetValue;

        // Always return a brush so the bound TextBlock never falls back to the theme default
        // (which can be black in dark mode when the visual tree skips the global style).
        var key = value is DiffStatus status
            ? status switch
            {
                DiffStatus.Missing => "RegDiffMissing",
                DiffStatus.Added => "RegDiffAdded",
                DiffStatus.Different => "RegDiffDifferent",
                _ => "RegForeground",
            }
            : "RegForeground";

        if (app.TryGetResource(key, app.ActualThemeVariant, out var brush) && brush is IBrush b) return b;
        return AvaloniaProperty.UnsetValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
