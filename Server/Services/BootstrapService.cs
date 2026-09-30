namespace Remotely.Server.Services;

public sealed class BootstrapService
{
    public Task EnsureBootstrapAdminAsync()
    {
        return Task.CompletedTask;
    }
}
