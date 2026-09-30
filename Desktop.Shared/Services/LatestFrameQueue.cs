using System.Threading.Channels;

namespace Remotely.Desktop.Shared.Services;

/// <summary>
/// A single-slot queue for desktop frames. Reliable writes apply backpressure.
/// Latest-frame writes replace a frame that is waiting in the slot, allowing
/// the stream to prefer freshness over completeness when every frame is a
/// self-contained full-screen image.
/// </summary>
public sealed class LatestFrameQueue<T>
{
    private readonly Channel<T> _channel = Channel.CreateBounded<T>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = true,
            AllowSynchronousContinuations = false
        });

    public async ValueTask<bool> WriteAsync(
        T item,
        bool replacePending,
        CancellationToken cancellationToken = default)
    {
        if (!replacePending)
        {
            await _channel.Writer.WriteAsync(item, cancellationToken);
            return false;
        }

        var droppedPending = false;
        while (true)
        {
            while (_channel.Reader.TryRead(out _))
            {
                droppedPending = true;
            }

            if (_channel.Writer.TryWrite(item))
            {
                return droppedPending;
            }

            if (!await _channel.Writer.WaitToWriteAsync(cancellationToken))
            {
                throw new ChannelClosedException();
            }
        }
    }

    public ValueTask<T> ReadAsync(CancellationToken cancellationToken = default)
        => _channel.Reader.ReadAsync(cancellationToken);

    public void Complete(Exception? error = null)
        => _channel.Writer.TryComplete(error);
}
