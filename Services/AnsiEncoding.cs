// The system's ANSI code page, the encoding of REGEDIT4 files.

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace RegCompare.Services;

/// <summary>
/// The system's ANSI code page (Windows-1252 on a Western European system): the encoding of the text
/// of <c>REGEDIT4</c> files and of their <c>hex(2)</c> / <c>hex(7)</c> data. .NET only knows these code
/// pages once <see cref="CodePagesEncodingProvider"/> is registered.
/// </summary>
public static class AnsiEncoding
{
    private static readonly Lazy<Encoding> Ansi = new(static () =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding((int)GetACP());
    });

    /// <summary>The encoding of the system's ANSI code page.</summary>
    public static Encoding Current => Ansi.Value;

    [DllImport("kernel32.dll")]
    private static extern uint GetACP();
}
