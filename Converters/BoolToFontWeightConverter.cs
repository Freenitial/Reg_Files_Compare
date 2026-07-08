// true -> Bold, anything else -> Normal. Used for diff-row emphasis.

using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace RegCompare.Converters;

/// <summary>
/// Returns <see cref="FontWeight.Bold"/> when the input bool is true, otherwise <see cref="FontWeight.Normal"/>.
/// </summary>
public sealed class BoolToFontWeightConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? FontWeight.Bold : FontWeight.Normal;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
