// true -> Strikethrough, false -> null. Used to render missing-status items.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace RegCompare.Converters;

/// <summary>
/// Returns <see cref="TextDecorations.Strikethrough"/> when the input bool is true,
/// otherwise <see cref="AvaloniaProperty.UnsetValue"/> so the default decoration applies.
/// </summary>
public sealed class BoolToTextDecorationsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? TextDecorations.Strikethrough : AvaloniaProperty.UnsetValue;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
