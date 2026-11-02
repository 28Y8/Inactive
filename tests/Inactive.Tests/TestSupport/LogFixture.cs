using Inactive.Core.Logging;
using Inactive.Tests.TestSupport;

[assembly: AssemblyFixture(typeof(LogFixture))]

namespace Inactive.Tests.TestSupport;

/// <summary>Keeps test runs from writing into the real log folder.</summary>
public sealed class LogFixture : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "InactiveTests", Guid.NewGuid().ToString("N"));

    public LogFixture() => AppLog.Configure(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Nothing was logged.
        }
        catch (IOException)
        {
            // A late log line can hold the folder open. It lives in the temp folder anyway.
        }
    }
}

/// <summary>A temporary folder that removes itself.</summary>
internal sealed class TemporaryDirectory : IDisposable
{
    public string Path { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "InactiveTests", Guid.NewGuid().ToString("N"));

    public TemporaryDirectory() => Directory.CreateDirectory(Path);

    public void Dispose() => Directory.Delete(Path, recursive: true);
}

internal static class Wait
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>Waits for a signal, and gives up early when the test run is cancelled.</summary>
    public static bool For(ManualResetEventSlim signal, TimeSpan? timeout = null) =>
        signal.Wait(timeout ?? Timeout, TestContext.Current.CancellationToken);

    /// <summary>Polls until the condition holds, for checks across real threads.</summary>
    public static bool Until(Func<bool> condition)
    {
        var deadline = Environment.TickCount64 + (long)Timeout.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(2);
        }

        return condition();
    }
}
