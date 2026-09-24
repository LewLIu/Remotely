namespace Remotely.Manager.Win.Services;

public interface IRemotelyServiceController
{
    Task<RemotelyServiceState> GetStateAsync(CancellationToken cancellationToken = default);
    Task StartAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
    Task StopAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}
