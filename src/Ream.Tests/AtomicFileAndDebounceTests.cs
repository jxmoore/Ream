using Ream.Core.Utilities;
using Ream.Persistence.Io;

namespace Ream.Tests;

public class AtomicFileTests
{
    [Fact]
    public void Write_CreatesDirectoriesAndFile()
    {
        using var dir = new TempDir();
        string path = dir.Combine("a", "b", "file.txt");

        AtomicFile.WriteAllText(path, "hello");

        Assert.Equal("hello", File.ReadAllText(path));
    }

    [Fact]
    public void Write_ReplacesExistingContentAndLeavesNoTempFile()
    {
        using var dir = new TempDir();
        string path = dir.Combine("file.txt");
        AtomicFile.WriteAllText(path, "old");

        AtomicFile.WriteAllText(path, "new");

        Assert.Equal("new", File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(dir.Path, "*.tmp"));
    }

    [Fact]
    public void Write_DoesNotPrependAByteOrderMark()
    {
        using var dir = new TempDir();
        string path = dir.Combine("file.txt");

        AtomicFile.WriteAllText(path, "x");

        Assert.Equal([(byte)'x'], File.ReadAllBytes(path));
    }
}

public class DebouncedActionTests
{
    [Fact]
    public async Task BurstOfTriggers_RunsOnceAfterTheQuietPeriod()
    {
        int runs = 0;
        using var debounced = new DebouncedAction(() => Interlocked.Increment(ref runs), TimeSpan.FromMilliseconds(80));

        for (int i = 0; i < 10; i++)
        {
            debounced.Trigger();
            await Task.Delay(10);
        }
        Assert.Equal(0, Volatile.Read(ref runs));

        await Task.Delay(400);
        Assert.Equal(1, Volatile.Read(ref runs));
    }

    [Fact]
    public async Task Flush_RunsPendingWorkImmediatelyAndOnlyOnce()
    {
        int runs = 0;
        using var debounced = new DebouncedAction(() => Interlocked.Increment(ref runs), TimeSpan.FromMilliseconds(80));

        debounced.Trigger();
        debounced.Flush();
        Assert.Equal(1, Volatile.Read(ref runs));

        await Task.Delay(300);
        Assert.Equal(1, Volatile.Read(ref runs));
    }

    [Fact]
    public void Flush_WithNothingPending_DoesNothing()
    {
        int runs = 0;
        using var debounced = new DebouncedAction(() => runs++, TimeSpan.FromMilliseconds(50));

        debounced.Flush();

        Assert.Equal(0, runs);
    }

    [Fact]
    public async Task Invoker_MarshalsTheRun()
    {
        var marshalled = new TaskCompletionSource();
        using var debounced = new DebouncedAction(() => { }, TimeSpan.FromMilliseconds(20), action =>
        {
            action();
            marshalled.TrySetResult();
        });

        debounced.Trigger();

        await marshalled.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Dispose_CancelsPendingWork()
    {
        int runs = 0;
        var debounced = new DebouncedAction(() => Interlocked.Increment(ref runs), TimeSpan.FromMilliseconds(50));

        debounced.Trigger();
        debounced.Dispose();

        await Task.Delay(250);
        Assert.Equal(0, Volatile.Read(ref runs));
    }
}
