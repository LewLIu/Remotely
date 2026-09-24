using Remotely.Desktop.Shared.Abstractions;
using Remotely.Shared.Enums;
using Remotely.Shared.Models;

namespace Remotely.Desktop.Shared.Services;

public interface IAudioPolicyController
{
    void SetViewerRequested(bool requested);
}

public sealed class AudioPolicyController : IAudioPolicyController, IDisposable
{
    private readonly IAudioCapturer _audioCapturer;
    private readonly IRemoteStreamSettingsProvider _settingsProvider;
    private bool _effectiveOn;
    private bool _viewerRequested;

    public AudioPolicyController(
        IAudioCapturer audioCapturer,
        IRemoteStreamSettingsProvider settingsProvider)
    {
        _audioCapturer = audioCapturer;
        _settingsProvider = settingsProvider;
        _settingsProvider.SettingsChanged += SettingsProvider_SettingsChanged;
    }

    public void Dispose()
    {
        _settingsProvider.SettingsChanged -= SettingsProvider_SettingsChanged;
        GC.SuppressFinalize(this);
    }

    public void SetViewerRequested(bool requested)
    {
        _viewerRequested = requested;
        Apply();
    }

    private void Apply()
    {
        var shouldRun =
            _settingsProvider.Current.AudioMode != RemoteAudioMode.Off &&
            _viewerRequested;

        if (shouldRun == _effectiveOn)
        {
            return;
        }

        _effectiveOn = shouldRun;
        _audioCapturer.ToggleAudio(shouldRun);
    }

    private void SettingsProvider_SettingsChanged(object? sender, RemoteStreamSettings settings)
    {
        Apply();
    }
}
