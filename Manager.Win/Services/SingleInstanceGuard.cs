namespace Remotely.Manager.Win.Services;

public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    private readonly bool _ownsMutex;
    private bool _disposed;

    public SingleInstanceGuard(string mutexName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);

        _mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        IsPrimaryInstance = createdNew;
        _ownsMutex = createdNew;
    }

    public bool IsPrimaryInstance { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_ownsMutex)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The owning thread has already released/abandoned the mutex.
            }
        }

        _mutex.Dispose();
        _disposed = true;
    }
}
