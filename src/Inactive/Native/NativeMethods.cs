using System.Runtime.InteropServices;

namespace Inactive.Native;

internal static partial class NativeMethods
{
    public const int GwlExStyle = -20;
    public const long WsExToolWindow = 0x00000080;
    public const long WsExNoActivate = 0x08000000;

    public const uint SwpNoSize = 0x0001;
    public const uint SwpNoMove = 0x0002;
    public const uint SwpNoActivate = 0x0010;

    public const uint ModNoRepeat = 0x4000;
    public const int WmHotkey = 0x0312;

    private const int SmMaximumTouches = 95;
    private const int SmConvertibleSlateMode = 0x2003;

    public static readonly nint HwndTopmost = -1;

    /// <summary>
    /// True when a touch device reports slate mode, which usually means no keyboard. The touch check
    /// matters: the slate metric does not apply to desktop PCs, so it means nothing there.
    /// </summary>
    public static bool IsInTabletMode() =>
        GetSystemMetrics(SmMaximumTouches) > 0 && GetSystemMetrics(SmConvertibleSlateMode) == 0;

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static partial nint GetWindowLongPtr(nint hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    public static partial nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int nIndex);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(nint hWnd, int id);
}
