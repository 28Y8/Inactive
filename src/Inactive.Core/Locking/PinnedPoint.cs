namespace Inactive.Core.Locking;

/// <summary>
/// The point the cursor is pinned to, published by CursorGuard and read by the hook callback.
/// Packed into one long so a reader never sees half of an update, without taking a lock.
/// </summary>
internal sealed class PinnedPoint
{
    private const long NotSet = long.MinValue;

    private long _packed = NotSet;

    public void Set(int x, int y) => Volatile.Write(ref _packed, Pack(x, y));

    public bool IsAt(int x, int y) => Volatile.Read(ref _packed) == Pack(x, y);

    private static long Pack(int x, int y) => ((long)x << 32) | (uint)y;
}
