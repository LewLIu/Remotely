using Remotely.Shared.Models;

namespace Remotely.Shared.Services;

public interface IEmergencySettingsStore
{
    RemoteStreamSettings Load(out string? warning);
    Task SaveAsync(RemoteStreamSettings settings, CancellationToken cancellationToken = default);
}
