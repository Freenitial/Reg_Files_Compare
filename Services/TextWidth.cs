// Width of a text as the values grids draw it.

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace RegCompare.Services;

/// <summary>
/// The width a text takes in the values grids (the theme's font, Inter, at 12 px), in bold: changed
/// rows are bold, and must fit too. A text on several lines measures its widest line. UI thread only.
/// </summary>
public static class TextWidth
{
    private const double FontSize = 12;

    /// <summary>How many texts, the widest by estimate, <see cref="Widest"/> measures exactly.</summary>
    private const int Candidates = 16;

    /// <summary>The grids' font, in bold, found the first time a text is measured.</summary>
    private static Typeface? _bold;

    /// <summary>The advance of each character met so far, measured once.</summary>
    private static readonly Dictionary<char, double> Advances = [];

    private static Typeface Bold => _bold ??= new Typeface(GridFont(), FontStyle.Normal, FontWeight.Bold);

    /// <summary>
    /// The font the grids draw with: the theme's <c>ContentControlThemeFontFamily</c> (the Fluent
    /// theme's Inter, the system font as fallback), not <see cref="FontFamily.Default"/> (Segoe UI).
    /// </summary>
    private static FontFamily GridFont()
    {
        var app = Application.Current;
        if (app is not null
            && app.TryGetResource("ContentControlThemeFontFamily", app.ActualThemeVariant, out var value)
            && value is FontFamily family)
        {
            return family;
        }
        return new FontFamily("fonts:Inter#Inter, $Default");
    }

    /// <summary>The exact width of <paramref name="text"/>.</summary>
    public static double Measure(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Bold, FontSize, null).Width;
    }

    /// <summary>
    /// The width of the widest of <paramref name="texts"/>. Shaping thousands of texts takes a while,
    /// so each is first estimated from its characters' advances, and only the widest few estimates
    /// are measured exactly.
    /// </summary>
    public static double Widest(IEnumerable<string> texts)
    {
        var widest = new PriorityQueue<string, double>();
        foreach (var text in texts)
        {
            var estimate = Estimate(text);
            if (widest.Count < Candidates) widest.Enqueue(text, estimate);
            else if (widest.TryPeek(out _, out var smallest) && estimate > smallest) widest.EnqueueDequeue(text, estimate);
        }

        var max = 0d;
        while (widest.TryDequeue(out var text, out _)) max = Math.Max(max, Measure(text));
        return max;
    }

    /// <summary>The sum of the characters' advances of the longest line (kerning aside).</summary>
    public static double Estimate(string text)
    {
        double line = 0, max = 0;
        foreach (var c in text)
        {
            if (c is '\n' or '\r')
            {
                max = Math.Max(max, line);
                line = 0;
                continue;
            }
            if (!Advances.TryGetValue(c, out var advance))
            {
                advance = Measure(c.ToString());
                Advances[c] = advance;
            }
            line += advance;
        }
        return Math.Max(max, line);
    }
}
