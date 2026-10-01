using Microsoft.Extensions.Time.Testing;
using Remotely.Desktop.Shared.Services;

namespace Remotely.Desktop.Win.Tests;

[TestClass]
public class FrameRateGateTests
{
    [TestMethod]
    public async Task EightFps_EnforcesAbout125MsBetweenFrameAttempts()
    {
        var fakeTime = new FakeTimeProvider();
        var gate = new FrameRateGate(fakeTime);

        await gate.WaitAsync(8);
        var second = gate.WaitAsync(8).AsTask();

        Assert.IsFalse(second.IsCompleted);
        fakeTime.Advance(TimeSpan.FromMilliseconds(124));
        Assert.IsFalse(second.IsCompleted);
        fakeTime.Advance(TimeSpan.FromMilliseconds(1));
        await second;
    }

    [TestMethod]
    public async Task OriginalNullCap_DoesNotDelayAndResetsPendingSchedule()
    {
        var fakeTime = new FakeTimeProvider();
        var gate = new FrameRateGate(fakeTime);

        await gate.WaitAsync(5);
        var delayed = gate.WaitAsync(5).AsTask();
        Assert.IsFalse(delayed.IsCompleted);

        await gate.WaitAsync(null);
        await gate.WaitAsync(null);

        fakeTime.Advance(TimeSpan.FromMilliseconds(200));
        await delayed;

        await gate.WaitAsync(5);
        var afterReset = gate.WaitAsync(5).AsTask();
        Assert.IsFalse(afterReset.IsCompleted);
        fakeTime.Advance(TimeSpan.FromMilliseconds(200));
        await afterReset;
    }

    [TestMethod]
    public async Task Cancellation_InterruptsPendingDelay()
    {
        var fakeTime = new FakeTimeProvider();
        var gate = new FrameRateGate(fakeTime);
        using var cts = new CancellationTokenSource();

        await gate.WaitAsync(2);
        var pending = gate.WaitAsync(2, cts.Token).AsTask();
        Assert.IsFalse(pending.IsCompleted);

        cts.Cancel();

        await Assert.ThrowsExceptionAsync<TaskCanceledException>(async () => await pending);
    }
}
