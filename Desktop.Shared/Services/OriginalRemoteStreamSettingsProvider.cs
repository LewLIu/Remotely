using Remotely.Desktop.Shared.Abstractions;
using Remotely.Shared.Models;

namespace Remotely.Desktop.Shared.Services;

public sealed class OriginalRemoteStreamSettingsProvider : IRemoteStreamSettingsProvider
{
    public RemoteStreamSettings Current => RemoteStreamSettings.Original;

    public event EventHandler<RemoteStreamSettings>? SettingsChanged
    {
        add { }
        remove { }
    }
}
