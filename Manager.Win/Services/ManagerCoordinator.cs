namespace Remotely.Manager.Win.Services;

public sealed class ManagerCoordinator
{
    private static readonly TimeSpan DefaultOperationTimeout = TimeSpan.FromSeconds(10);
    private readonly IRemotelyServiceController _serviceController;

    public ManagerCoordinator(IRemotelyServiceController serviceController)
    {
        _serviceController = serviceController;
    }

    public RemotelyServiceState CurrentState { get; private set; } = RemotelyServiceState.Error;
    public string? LastError { get; private set; }

    public event EventHandler<RemotelyServiceState>? StateChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await RefreshAsync(cancellationToken);
        if (CurrentState == RemotelyServiceState.Stopped)
        {
            await StartServiceAsync(DefaultOperationTimeout, cancellationToken);
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var state = await _serviceController.GetStateAsync(cancellationToken);
            if (state == RemotelyServiceState.Missing)
            {
                SetError("Remotely_Service is not installed.");
                return;
            }

            SetState(state);
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
    }

    public async Task<bool> StartServiceAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var state = await _serviceController.GetStateAsync(cancellationToken);
            if (state == RemotelyServiceState.Missing)
            {
                SetError("Remotely_Service is not installed.");
                return false;
            }

            if (state == RemotelyServiceState.Running)
            {
                SetState(state);
                return true;
            }

            await _serviceController.StartAsync(timeout ?? DefaultOperationTimeout, cancellationToken);
            await RefreshAsync(cancellationToken);
            return CurrentState == RemotelyServiceState.Running;
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
            return false;
        }
    }

    public async Task<bool> StopServiceAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var state = await _serviceController.GetStateAsync(cancellationToken);
            if (state == RemotelyServiceState.Missing)
            {
                SetError("Remotely_Service is not installed.");
                return false;
            }

            if (state == RemotelyServiceState.Stopped)
            {
                SetState(state);
                return true;
            }

            await _serviceController.StopAsync(timeout ?? DefaultOperationTimeout, cancellationToken);
            await RefreshAsync(cancellationToken);
            return CurrentState == RemotelyServiceState.Stopped;
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
            return false;
        }
    }

    public async Task<bool> TryExitAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var state = await _serviceController.GetStateAsync(cancellationToken);
            if (state == RemotelyServiceState.Missing)
            {
                SetState(RemotelyServiceState.Stopped);
                return true;
            }

            if (state == RemotelyServiceState.Stopped)
            {
                SetState(state);
                return true;
            }

            await _serviceController.StopAsync(timeout, cancellationToken);
            await RefreshAsync(cancellationToken);
            return CurrentState == RemotelyServiceState.Stopped;
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
            return false;
        }
    }

    private void SetError(string message)
    {
        LastError = message;
        SetState(RemotelyServiceState.Error, clearError: false);
    }

    private void SetState(RemotelyServiceState state, bool clearError = true)
    {
        if (clearError)
        {
            LastError = null;
        }

        if (CurrentState == state)
        {
            return;
        }

        CurrentState = state;
        StateChanged?.Invoke(this, state);
    }
}
