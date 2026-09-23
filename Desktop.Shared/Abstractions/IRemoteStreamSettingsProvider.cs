using Remotely.Shared.Models;

namespace Remotely.Desktop.Shared.Abstractions;

public interface IRemoteStreamSettingsProvider
{
    RemoteStreamSettings Current { get; }
    event EventHandler<RemoteStreamSettings>? SettingsChanged;
}
