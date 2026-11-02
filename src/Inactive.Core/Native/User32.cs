using System.Runtime.InteropServices;

namespace Inactive.Core.Native;

internal static unsafe partial class User32
{
    private const string Library = "user32.dll";

    [LibraryImport(Library, SetLastError = true)]
    public static partial nint SetWindowsHookExW(
        int idHook,
        delegate* unmanaged<int, nint, nint, nint> lpfn,
        nint hmod,
        uint dwThreadId);

    [LibraryImport(Library, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWindowsHookEx(nint hhk);

    [LibraryImport(Library)]
    public static partial nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [LibraryImport(Library, SetLastError = true)]
    public static partial int GetMessageW(MSG* lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [LibraryImport(Library)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PeekMessageW(MSG* lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [LibraryImport(Library, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostThreadMessageW(uint idThread, uint msg, nuint wParam, nint lParam);

    [LibraryImport(Library, SetLastError = true)]
    public static partial nuint SetTimer(nint hWnd, nuint nIDEvent, uint uElapse, nint lpTimerFunc);

    [LibraryImport(Library)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool KillTimer(nint hWnd, nuint uIDEvent);

    [LibraryImport(Library)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetCursorPos(int x, int y);

    [LibraryImport(Library)]
    public static partial int GetSystemMetrics(int nIndex);

    [LibraryImport(Library)]
    public static partial short GetAsyncKeyState(int vKey);

    [LibraryImport(Library)]
    public static partial uint MapVirtualKeyW(uint uCode, uint uMapType);

    [LibraryImport(Library)]
    public static partial nint GetForegroundWindow();

    [LibraryImport(Library)]
    public static partial uint GetWindowThreadProcessId(nint hWnd, uint* lpdwProcessId);
}
