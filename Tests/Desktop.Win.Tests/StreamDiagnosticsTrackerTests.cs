using Microsoft.Extensions.Time.Testing;
using Remotely.Desktop.Shared.Services;

namespace Remotely.Desktop.Win.Tests;

[TestClass]
public class StreamDiagnosticsTrackerTests
{
    [TestMethod]
    public void FiveSecondWindow_ReportsTransportAndEncodeMetrics()
    {
        var time = new FakeTimeProvider();
        var tracker = new StreamDiagnosticsTracker(time, TimeSpan.FromSeconds(5));

        Assert.IsNull(tracker.RecordFrame(2560, 1440, 960, 540, 1024, TimeSpan.FromMilliseconds(8)));
        time.Advance(TimeSpan.FromSeconds(5));
        var snapshot = tracker.RecordFrame(2560, 1440, 960, 540, 2048, TimeSpan.FromMilliseconds(12));

        Assert.IsNotNull(snapshot);
        Assert.AreEqual(2560, snapshot.SourceWidth);
        Assert.AreEqual(1440, snapshot.SourceHeight);
        Assert.AreEqual(960, snapshot.StreamWidth);
        Assert.AreEqual(540, snapshot.StreamHeight);
        Assert.AreEqual(0.4, snapshot.ActualFps, 0.001);
        Assert.AreEqual(0.6, snapshot.EncodedKBytesPerSecond, 0.001);
        Assert.AreEqual(1.5, snapshot.AverageFrameKBytes, 0.001);
        Assert.AreEqual(10.0, snapshot.AverageEncodeMilliseconds, 0.001);
    }

    [TestMethod]
    public void Snapshot_ResetsTheNextWindow()
    {
        var time = new FakeTimeProvider();
        var tracker = new StreamDiagnosticsTracker(time, TimeSpan.FromSeconds(5));
        tracker.RecordFrame(1920, 1080, 1280, 720, 1024, TimeSpan.FromMilliseconds(10));
        time.Advance(TimeSpan.FromSeconds(5));
        Assert.IsNotNull(tracker.RecordFrame(1920, 1080, 1280, 720, 1024, TimeSpan.FromMilliseconds(10)));

        Assert.IsNull(tracker.RecordFrame(1920, 1080, 1280, 720, 4096, TimeSpan.FromMilliseconds(20)));
    }
}
