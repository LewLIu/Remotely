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

    public async Task EnsureManualStartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // The current installer already creates Remotely_Service as Manual. Avoid running
        // sc.exe on every Manager launch when the desired state is already satisfied.
        using (var service = new ServiceController(ServiceName))
        {
            service.Refresh();
            if (!ServiceStartupPolicy.RequiresManualConfiguration(service.StartType))
            {
                return;
            }
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "sc.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("config");
        startInfo.ArgumentList.Add(ServiceName);
        startInfo.ArgumentList.Add("start=");
        startInfo.ArgumentList.Add("demand");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start sc.exe to configure Remotely_Service.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException(
                $"Unable to set {ServiceName} startup type to Manual. sc.exe exit code {process.ExitCode}. {detail}".Trim());
        }

        using var verification = new ServiceController(ServiceName);
        verification.Refresh();
        if (verification.StartType != ServiceStartMode.Manual)
        {
            throw new InvalidOperationException(
                $"Unable to confirm {ServiceName} startup type is Manual after sc.exe completed successfully.");
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
                throw new System.TimeoutException(
                    $"Timed out waiting for {ServiceName} to reach {desired}.");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }
}
