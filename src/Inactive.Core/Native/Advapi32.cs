using System.Runtime.InteropServices;

namespace Inactive.Core.Native;

internal static unsafe partial class Advapi32
{
    private const string Library = "advapi32.dll";

    [LibraryImport(Library, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenProcessToken(nint ProcessHandle, uint DesiredAccess, nint* TokenHandle);

    [LibraryImport(Library, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetTokenInformation(
        nint TokenHandle,
        int TokenInformationClass,
        void* TokenInformation,
        uint TokenInformationLength,
        uint* ReturnLength);
}
