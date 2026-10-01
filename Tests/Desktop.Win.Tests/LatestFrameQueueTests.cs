using Remotely.Desktop.Shared.Services;

namespace Remotely.Desktop.Win.Tests;

[TestClass]
public class LatestFrameQueueTests
{
    [TestMethod]
    public async Task ReplacePending_KeepsOnlyNewestFrame()
    {
        var queue = new LatestFrameQueue<string>();

        Assert.IsFalse(await queue.WriteAsync("frame-1", replacePending: true));
        Assert.IsTrue(await queue.WriteAsync("frame-2", replacePending: true));
        Assert.IsTrue(await queue.WriteAsync("frame-3", replacePending: true));

        Assert.AreEqual("frame-3", await queue.ReadAsync());
    }

    [TestMethod]
    public async Task ReliableWrite_WaitsInsteadOfDroppingPendingFrame()
    {
        var queue = new LatestFrameQueue<int>();
        Assert.IsFalse(await queue.WriteAsync(1, replacePending: false));

        var secondWrite = queue.WriteAsync(2, replacePending: false).AsTask();
        await Task.Delay(50);
        Assert.IsFalse(secondWrite.IsCompleted, "Reliable mode must apply backpressure rather than replace a pending frame.");

        Assert.AreEqual(1, await queue.ReadAsync());
        Assert.IsFalse(await secondWrite);
        Assert.AreEqual(2, await queue.ReadAsync());
    }
}
