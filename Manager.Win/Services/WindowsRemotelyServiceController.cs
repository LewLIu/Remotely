using System.Diagnostics;
using System.ServiceProcess;

namespace Remotely.Manager.Win.Services;

public sealed class WindowsRemotelyServiceController : IRemotelyServiceController
{
    public const string ServiceName = "Remotely_Service";
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    public Task<RemotelyServiceState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var service = new ServiceController(ServiceName);
            service.Refresh();
            return Task.FromResult(Map(service.Status));
        }
        catch (InvalidOperationException)
        {
            return Task.FromResult(RemotelyServiceState.Missing);
        }
    }

    public async Task StartAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var service = new ServiceController(ServiceName);
        service.Refresh();

        if (service.Status == ServiceControllerStatus.Running)
        {
            return;
        }

        if (service.Status == ServiceControllerStatus.StopPending)
        {
            await WaitForStatusAsync(service, ServiceControllerStatus.Stopped, timeout, cancellationToken);
        }

        service.Refresh();
        if (service.Status == ServiceControllerStatus.Stopped)
        {
            service.Start();
        }

        await WaitForStatusAsync(service, ServiceControllerStatus.Running, timeout, cancellationToken);
    }

    public async Task StopAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var service = new ServiceController(ServiceName);
        service.Refresh();

        if (service.Status == ServiceControllerStatus.Stopped)
        {
            return;
        }

        if (service.Status == ServiceControllerStatus.StartPending)
        {
            await WaitForStatusAsync(service, ServiceControllerStatus.Running, timeout, cancellationToken);
        }

        service.Refresh();
        if (service.Status == ServiceControllerStatus.Running)
        {
            service.Stop();
        }

        await WaitForStatusAsync(service, ServiceControllerStatus.Stopped, timeout, cancellationToken);
    }

    private static RemotelyServiceState Map(ServiceControllerStatus status)
        => status switch
        {
            ServiceControllerStatus.Stopped => RemotelyServiceState.Stopped,
            ServiceControllerStatus.StartPending => RemotelyServiceState.StartPending,
            ServiceControllerStatus.Running => RemotelyServiceState.Running,
            ServiceControllerStatus.StopPending => RemotelyServiceState.StopPending,
            _ => RemotelyServiceState.Error
        };

    private static async Task WaitForStatusAsync(
        ServiceController service,
        ServiceControllerStatus desired,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            service.Refresh();
            if (service.Status == desired)
            {
                return;
            }

            if (stopwatch.Elapsed >= timeout)
            {
                throw new TimeoutException(
                    $"Timed out waiting for {ServiceName} to reach {desired}.");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }
}
