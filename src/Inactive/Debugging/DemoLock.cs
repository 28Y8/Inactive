#if DEBUG
using System.Globalization;
using System.IO;
using Inactive.Core.Locking;
using Inactive.Services;

namespace Inactive.Debugging;

/// <summary>
/// The smoke test behind --demo-lock: locks at startup, releases after a set time and writes what
/// happened to the --demo-log file. Each line is written as it happens, so a crash leaves a trail.
/// </summary>
internal sealed class DemoLock(LockController engine, LockCoordinator locks, int seconds, string? logPath, Action finished)
{
    private Timer? _timer;

    public static void ReportAlreadyRunning(string? logPath) =>
        Write(logPath, "result: not started, Inactive is already running");

    public void Start()
    {
        locks.Released += OnReleased;

        var result = locks.Engage();
        Write(logPath, result.Succeeded
            ? "hook install: ok"
            : string.Create(CultureInfo.InvariantCulture, $"hook install: failed, {result.Error}, Win32 error {result.Win32Error}"));

        if (!result.Succeeded)
        {
            finished();
            return;
        }

        // Runs on a pool thread, so the release is exercised without the UI thread.
        _timer = new Timer(
            _ => engine.Release(UnlockReason.DemoTimer),
            null,
            TimeSpan.FromSeconds(seconds),
            Timeout.InfiniteTimeSpan);
    }

    private void OnReleased(object? sender, UnlockedEventArgs e)
    {
        locks.Released -= OnReleased;
        _timer?.Dispose();

        var stats = engine.LastSessionStats;
        var cursor = System.Windows.Forms.Cursor.Position;

        Write(logPath, string.Create(CultureInfo.InvariantCulture, $"keyboard events swallowed: {stats.KeyboardEventsSwallowed}"));
        Write(logPath, string.Create(CultureInfo.InvariantCulture, $"mouse events swallowed: {stats.MouseEventsSwallowed}"));
        Write(logPath, $"ended by: {e.Reason}");
        Write(logPath, string.Create(CultureInfo.InvariantCulture, $"final cursor position: {cursor.X},{cursor.Y}"));

        finished();
    }

    private static void Write(string? path, string line)
    {
        if (path is not null)
        {
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }
}
#endif
