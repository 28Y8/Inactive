using Inactive.Core.Logging;
using Inactive.Tests.TestSupport;

namespace Inactive.Tests.Logging;

[CollectionDefinition(nameof(AppLogCollection), DisableParallelization = true)]
public sealed class AppLogCollection;

// These tests point the shared log at their own folder, so nothing may run beside them.
[Collection(nameof(AppLogCollection))]
public sealed class AppLogTests : IDisposable
{
    private readonly TemporaryDirectory _directory = new();
    private readonly string _previous = AppLog.LogDirectory;

    public AppLogTests() => AppLog.Configure(_directory.Path);

    public void Dispose()
    {
        AppLog.Configure(_previous);
        _directory.Dispose();
    }

    private string LogFile => Path.Combine(_directory.Path, "inactive.log");

    private string RolledFile => Path.Combine(_directory.Path, "inactive.1.log");

    [Fact]
    public void Default_folder_is_under_appdata()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Inactive", "logs");

        Assert.Equal(expected, AppLog.DefaultDirectory);
    }

    [Fact]
    public void Writes_level_and_message()
    {
        AppLog.Info("hello");
        AppLog.Warn("careful");
        AppLog.Error("broken");

        var lines = File.ReadAllLines(LogFile);
        Assert.Contains(lines, line => line.EndsWith("INFO hello", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.EndsWith("WARN careful", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.EndsWith("ERROR broken", StringComparison.Ordinal));
    }

    [Fact]
    public void Includes_exception_details()
    {
        AppLog.Error("failed", new InvalidOperationException("the reason"));

        Assert.Contains("the reason", File.ReadAllText(LogFile), StringComparison.Ordinal);
    }

    [Fact]
    public void Creates_the_folder()
    {
        var nested = Path.Combine(_directory.Path, "a", "b");
        AppLog.Configure(nested);

        AppLog.Info("hello");

        Assert.True(File.Exists(Path.Combine(nested, "inactive.log")));
    }

    [Fact]
    public void Rolls_over_at_the_cap_and_keeps_one_old_file()
    {
        var line = new string('x', 10_000);

        for (var i = 0; i < 350; i++)
        {
            AppLog.Info(line);
        }

        Assert.True(File.Exists(RolledFile));
        Assert.True(new FileInfo(LogFile).Length <= AppLog.MaxFileBytes);
        Assert.True(new FileInfo(RolledFile).Length <= AppLog.MaxFileBytes);
        Assert.Equal(2, Directory.GetFiles(_directory.Path).Length);
    }

    [Fact]
    public void Survives_an_unusable_folder()
    {
        var file = Path.Combine(_directory.Path, "a file");
        File.WriteAllText(file, string.Empty);
        AppLog.Configure(Path.Combine(file, "nested"));

        var error = Record.Exception(() => AppLog.Info("hello"));

        Assert.Null(error);
    }

    [Fact]
    public void Writing_from_many_threads_loses_nothing()
    {
        Parallel.For(0, 200, i => AppLog.Info($"line {i}"));

        Assert.Equal(200, File.ReadAllLines(LogFile).Length);
    }
}
