#if DEBUG
using System.Globalization;

#endif
namespace Inactive;

/// <summary>What the command line and the installer asked for.</summary>
internal sealed class StartupOptions
{
    private const string BackgroundFlag = "--background";

#if DEBUG
    private const string DemoLockFlag = "--demo-lock";
    private const string DemoLogFlag = "--demo-log";
#endif

    private StartupOptions()
    {
    }

    /// <summary>Stay in the tray and show no window.</summary>
    public bool StartHidden { get; private init; }

#if DEBUG
    /// <summary>Lock at startup and release after this many seconds.</summary>
    public int? DemoLockSeconds { get; private init; }

    /// <summary>The file that receives the result of the demo lock.</summary>
    public string? DemoLogPath { get; private init; }
#endif

    public static StartupOptions Parse(string[] args, bool isFirstRun)
    {
#if DEBUG
        int? demoSeconds = null;
        string? demoLog = null;

        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(DemoLockFlag, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
            {
                demoSeconds = seconds;
            }
            else if (args[i].Equals(DemoLogFlag, StringComparison.OrdinalIgnoreCase))
            {
                demoLog = args[i + 1];
            }
        }
#endif

        return new StartupOptions
        {
            // Right after installing, the person expects to see the app they just installed.
            StartHidden = !isFirstRun && args.Contains(BackgroundFlag, StringComparer.OrdinalIgnoreCase),
#if DEBUG
            DemoLockSeconds = demoSeconds,
            DemoLogPath = demoLog,
#endif
        };
    }

    /// <summary>Returns the exit code for a launch that found the app already running.</summary>
    public int ReportSecondInstance()
    {
#if DEBUG
        if (DemoLockSeconds is not null)
        {
            Debugging.DemoLock.ReportAlreadyRunning(DemoLogPath);
            return 1;
        }
#endif
        return 0;
    }
}
