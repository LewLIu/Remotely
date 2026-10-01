namespace Remotely.Desktop.Shared.Services;

public sealed class FrameRateGate
{
    private readonly TimeProvider _timeProvider;
    private DateTimeOffset? _nextFrameAt;
    private int _scheduleVersion;

    public FrameRateGate()
        : this(TimeProvider.System)
    {
    }

    public FrameRateGate(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public async ValueTask WaitAsync(int? maxFps, CancellationToken cancellationToken = default)
    {
        if (maxFps is null)
        {
            Interlocked.Increment(ref _scheduleVersion);
            _nextFrameAt = null;
            return;
        }

        if (maxFps <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFps));
        }

        var scheduleVersion = Volatile.Read(ref _scheduleVersion);
        var interval = TimeSpan.FromSeconds(1d / maxFps.Value);
        var now = _timeProvider.GetUtcNow();

        if (_nextFrameAt is { } nextFrameAt && nextFrameAt > now)
        {
            await Task.Delay(nextFrameAt - now, _timeProvider, cancellationToken);
        }

        if (scheduleVersion != Volatile.Read(ref _scheduleVersion))
        {
            return;
        }

        _nextFrameAt = _timeProvider.GetUtcNow() + interval;
    }
}
