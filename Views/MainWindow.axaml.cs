// Code-behind for the main window. Handles only View-level concerns: file picker invocation,
// Enter-to-search shortcut, the OS drag-drop integration (Avalonia 12 IDataTransfer API),
// and forcing the dark/light Win32 title bar via DwmSetWindowAttribute + a forced non-client repaint.

using System;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using RegCompare.ViewModels;

namespace RegCompare.Views;

public partial class MainWindow : Window
{
    // DWM dark-mode attribute changed ID across Windows 10 builds: 19 prior to build 18985 (20H1),
    // then 20 from 20H1 onward. We set both for safety - the unrecognized one is silently ignored.
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    private const uint RDW_INVALIDATE = 0x0001;
    private const uint RDW_ERASE = 0x0004;
    private const uint RDW_UPDATENOW = 0x0100;
    private const uint RDW_FRAME = 0x0400;

    private const uint WM_NCACTIVATE = 0x0086;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        DragDrop.SetAllowDrop(this, true);

        // Apply the dark mode hint as early as we have a platform handle (before the first paint
        // if possible), then again on Opened, so the title bar never flashes white at startup.
        AttachedToVisualTree += OnAttachedToVisualTreeFirstTime;
    }

    private void OnAttachedToVisualTreeFirstTime(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        AttachedToVisualTree -= OnAttachedToVisualTreeFirstTime;
        ApplyWin32TitleBarTheme();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ApplyWin32TitleBarTheme();

        // Subscribe to the ThemeService event we control directly (Avalonia's
        // ActualThemeVariantChanged isn't always raised when RequestedThemeVariant is set
        // programmatically inside the same dispatcher tick). DataContext is guaranteed to
        // be set by App.OnFrameworkInitializationCompleted before the window is shown.
        if (DataContext is MainWindowViewModel vm)
        {
            vm.Theme.ThemeApplied += (_, _) => ApplyWin32TitleBarTheme();
        }
    }

    private void ApplyWin32TitleBarTheme()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (TryGetPlatformHandle() is not { } handle) return;

        var hwnd = handle.Handle;

        // Defer to Background priority so Avalonia has finished its own re-layout and re-paint
        // for the new theme variant before we tell DWM to repaint the chrome. Without this,
        // re-toggling Dark Theme from the running window leaves the chrome on its previous color
        // until the user manually moves or resizes the window.
        Dispatcher.UIThread.Post(() => ApplyTitleBarThemeNow(hwnd), DispatcherPriority.Background);
    }

    private static void ApplyTitleBarThemeNow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        var isDark = Application.Current?.ActualThemeVariant == ThemeVariant.Dark ? 1 : 0;

        // Try both attribute IDs - whichever the running Windows build doesn't recognize
        // is silently no-op'd, the right one wins.
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref isDark, sizeof(int));
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref isDark, sizeof(int));

        // Force a non-client repaint. SetWindowPos+RedrawWindow are needed for the *first*
        // paint at startup but are NOT enough to make DWM re-read the dark-mode attribute on
        // a runtime toggle - DWM only picks up the new color scheme when the window goes
        // through an activation transition (the user's manual workaround: focus another
        // window then focus this one back).
        //
        // We simulate that activation cycle with two WM_NCACTIVATE messages: first
        // "deactivated" (wParam=0) then "active" (wParam=1). DefWindowProc forwards the
        // active-state change to DWM, which redraws the chrome with the new dark/light
        // attribute. Both messages are SendMessage (synchronous) so the deactivated state
        // never reaches a paint cycle - no visible flicker.
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);

        SendMessageW(hwnd, WM_NCACTIVATE, IntPtr.Zero, IntPtr.Zero);
        SendMessageW(hwnd, WM_NCACTIVATE, new IntPtr(1), IntPtr.Zero);

        RedrawWindow(hwnd, IntPtr.Zero, IntPtr.Zero,
            RDW_FRAME | RDW_INVALIDATE | RDW_ERASE | RDW_UPDATENOW);
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        await vm.BrowseFilesCommand.ExecuteAsync(this);
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (DataContext is not MainWindowViewModel vm) return;
        vm.AddSearchColumnCommand.Execute(null);
        e.Handled = true;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (!e.DataTransfer.Contains(DataFormat.File)) return;

        var files = e.DataTransfer.TryGetFiles();
        if (files is null) return;

        var paths = files
            .Select(static f => f.TryGetLocalPath())
            .Where(static p => !string.IsNullOrEmpty(p))
            .Cast<string>()
            .ToList();

        if (paths.Count > 0) vm.HandleDroppedPaths(paths);
    }
}
