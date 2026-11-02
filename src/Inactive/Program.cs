using System.Runtime.CompilerServices;
using Inactive.Startup;
using Velopack;

namespace Inactive;

internal static class Program
{
    private const string InstanceMutexName = @"Local\Inactive.SingleInstance";
    private const string ShowWindowEventName = @"Local\Inactive.ShowWindow";

    private static bool _isFirstRun;

    [STAThread]
    private static int Main(string[] args)
    {
        // Applying an update ends every running copy, and the running copy may be holding a lock.
        // So only the first instance may apply a waiting update. A second launch just signals it.
        var alreadyRunning = Mutex.TryOpenExisting(InstanceMutexName, out var running);
        running?.Dispose();

        // Velopack may run a hook and exit here, so this comes before anything touches WPF.
        VelopackApp.Build()
            .SetAutoApplyOnStartup(!alreadyRunning)
            .OnBeforeUninstallFastCallback(_ => StartupRegistration.RemoveQuietly())
            .OnFirstRun(_ => _isFirstRun = true)
            .Run();

        return RunApp(StartupOptions.Parse(args, _isFirstRun));
    }

    // Kept out of Main so that the JIT does not load the WPF assemblies before Velopack has run.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunApp(StartupOptions options)
    {
        using var instance = new Mutex(initiallyOwned: true, InstanceMutexName, out var isFirstInstance);
        using var showWindow = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowEventName);

        if (!isFirstInstance)
        {
            showWindow.Set();
            return options.ReportSecondInstance();
        }

        var app = new App(options, showWindow);
        app.InitializeComponent();
        return app.Run();
    }
}
