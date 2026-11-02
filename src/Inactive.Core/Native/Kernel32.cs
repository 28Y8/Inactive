using System.Runtime.InteropServices;

namespace Inactive.Core.Native;

internal static partial class Kernel32
{
    private const string Library = "kernel32.dll";

    [LibraryImport(Library)]
    public static partial uint GetCurrentThreadId();

    [LibraryImport(Library, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint GetModuleHandleW(string? lpModuleName);

    [LibraryImport(Library)]
    public static partial uint SetThreadExecutionState(uint esFlags);

    [LibraryImport(Library, SetLastError = true)]
    public static partial nint OpenProcess(
        uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        uint dwProcessId);

    [LibraryImport(Library, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint hObject);
}
