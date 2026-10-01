namespace Remotely.Desktop.Shared.Services;

public sealed record StreamDiagnosticsSnapshot(
    int SourceWidth,
    int SourceHeight,
    int StreamWidth,
    int StreamHeight,
    double ActualFps,
    double EncodedKBytesPerSecond,
    double AverageFrameKBytes,
    double AverageEncodeMilliseconds);

public sealed class StreamDiagnosticsTracker
{
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _windowLength;
    private DateTimeOffset _windowStart;
    private long _encodedBytes;
    private double _encodeMilliseconds;
    private int _frameCount;

    public StreamDiagnosticsTracker(TimeProvider timeProvider, TimeSpan? windowLength = null)
    {
        _timeProvider = timeProvider;
        _windowLength = windowLength ?? TimeSpan.FromSeconds(5);
        if (_windowLength <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(windowLength));
        _windowStart = _timeProvider.GetUtcNow();
    }

    public StreamDiagnosticsSnapshot? RecordFrame(
        int sourceWidth,
        int sourceHeight,
        int streamWidth,
        int streamHeight,
        int encodedBytes,
        TimeSpan encodeDuration)
    {
        _frameCount++;
        _encodedBytes += encodedBytes;
        _encodeMilliseconds += encodeDuration.TotalMilliseconds;

        var now = _timeProvider.GetUtcNow();
        var elapsed = now - _windowStart;
        if (elapsed < _windowLength)
        {
            return null;
        }

        var seconds = Math.Max(elapsed.TotalSeconds, 0.001);
        var snapshot = new StreamDiagnosticsSnapshot(
            sourceWidth,
            sourceHeight,
            streamWidth,
            streamHeight,
            _frameCount / seconds,
            (_encodedBytes / 1024d) / seconds,
            (_encodedBytes / 1024d) / _frameCount,
            _encodeMilliseconds / _frameCount);

        _windowStart = now;
        _frameCount = 0;
        _encodedBytes = 0;
        _encodeMilliseconds = 0;
        return snapshot;
    }
}
