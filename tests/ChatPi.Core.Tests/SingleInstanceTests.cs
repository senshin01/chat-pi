using ChatPi.App.Services;

namespace ChatPi.Core.Tests;

public sealed class SingleInstanceTests
{
    private sealed record LaunchMessage(string? Path);

    [Fact]
    public async Task SecondInstanceForwardsAUnicodePathAndKeepsListening()
    {
        // Own and release the mutex on the same worker thread. The test is also
        // compiled into the Windows suite to exercise Windows kernel primitives.
        var id = $"test-{Guid.NewGuid():N}";
        using var delivered = new System.Collections.Concurrent.BlockingCollection<LaunchMessage>();
        using var stop = new ManualResetEventSlim();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var primaryTask = Task.Run(() =>
        {
            try
            {
                using var primary = new SingleInstanceHost(id);
                Assert.True(primary.IsPrimary);
                primary.StartListening(path => { delivered.Add(new LaunchMessage(path)); return Task.CompletedTask; });
                ready.SetResult();
                if (!stop.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException();
            }
            catch (Exception exception) { ready.TrySetException(exception); throw; }
        });
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var expected = Path.GetFullPath("曲名 with spaces.wav");
            using var secondary = new SingleInstanceHost(id);
            Assert.False(secondary.IsPrimary);
            await secondary.SendAsync(expected);
            Assert.True(delivered.TryTake(out var actual));
            Assert.Equal(expected, actual.Path);
            await secondary.SendAsync(null);
            Assert.True(delivered.TryTake(out var focusOnly));
            Assert.Null(focusOnly.Path);
            for (var index = 0; index < 20; index++)
            {
                var next = Path.GetFullPath($"続けて開く {index}.wav");
                await secondary.SendAsync(next);
                Assert.True(delivered.TryTake(out var message));
                Assert.Equal(next, message.Path);
            }
        }
        finally { stop.Set(); await primaryTask; }
    }
}
