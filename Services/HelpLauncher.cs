// Opens the documentation (docs/help.html, embedded in the executable) in the default browser.

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace RegCompare.Services;

/// <summary>
/// Opens the documentation in the default browser. The page (docs/help.html, English and French) is
/// embedded in the executable: it is written to %TEMP%\Reg_Files_Compare\ (again on every opening, so
/// it always matches the running version), then opened through the shell. Nothing is downloaded.
/// </summary>
public static class HelpLauncher
{
    /// <summary>LogicalName of the embedded page (RegCompare.csproj).</summary>
    private const string ResourceName = "help.html";

    private const string PageName = "REG Files Compare - Help.html";

    /// <summary>Write the page and open it. Throws when either fails: the caller tells the user.</summary>
    public static void Open()
    {
        var path = WritePage(Path.Combine(Path.GetTempPath(), "Reg_Files_Compare"));
        using var browser = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    /// <summary>Where the page shows the version of the application that wrote it.</summary>
    private const string VersionToken = "{{VERSION}}";

    /// <summary>The application's version, "1.2.3".</summary>
    public static string Version
    {
        get
        {
            var assembly = typeof(HelpLauncher).Assembly;
            var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? assembly.GetName().Version?.ToString(3) ?? "";
            var plus = version.IndexOf('+');
            return plus < 0 ? version : version[..plus];
        }
    }

    /// <summary>Write the embedded page into <paramref name="directory"/> (with the running version in
    /// it) and return its path.</summary>
    public static string WritePage(string directory)
    {
        string html;
        using (var page = typeof(HelpLauncher).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("The help page is missing from the executable."))
        using (var reader = new StreamReader(page, Encoding.UTF8))
        {
            html = reader.ReadToEnd().Replace(VersionToken, Version, StringComparison.Ordinal);
        }
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, PageName);
        try
        {
            using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(html);
            file.Write(bytes);
        }
        catch (IOException) when (File.Exists(path))
        {
            // Another instance is writing the same page at this very moment: open that one.
        }
        return path;
    }
}
