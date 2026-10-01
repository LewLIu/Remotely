using Moq;
using Remotely.Desktop.Shared.Abstractions;
using Remotely.Desktop.Shared.Services;
using Remotely.Shared.Models;

namespace Remotely.Desktop.Win.Tests;

[TestClass]
public class AudioPolicyControllerTests
{
    [TestMethod]
    public void LowBandwidthProfile_SuppressesViewerAudioRequest_AndOriginalRestoresIt()
    {
        var capturer = new Mock<IAudioCapturer>();
        var settings = new MutableStreamSettingsProvider(RemoteStreamSettings.Original);
        using var policy = new AudioPolicyController(capturer.Object, settings);

        policy.SetViewerRequested(true);
        capturer.Verify(x => x.ToggleAudio(true), Times.Once);

        settings.Set(RemoteStreamSettings.Emergency);
        capturer.Verify(x => x.ToggleAudio(false), Times.Once);

        settings.Set(RemoteStreamSettings.Original);
        capturer.Verify(x => x.ToggleAudio(true), Times.Exactly(2));
    }

    [TestMethod]
    public void RepeatedSameEffectiveState_DoesNotRestartCapture()
    {
        var capturer = new Mock<IAudioCapturer>();
        var settings = new MutableStreamSettingsProvider(RemoteStreamSettings.Original);
        using var policy = new AudioPolicyController(capturer.Object, settings);

        policy.SetViewerRequested(true);
        policy.SetViewerRequested(true);
        settings.Set(RemoteStreamSettings.Balanced);
        settings.Set(RemoteStreamSettings.Emergency);
        settings.Set(RemoteStreamSettings.UltraLow);

        capturer.Verify(x => x.ToggleAudio(true), Times.Once);
        capturer.Verify(x => x.ToggleAudio(false), Times.Once);
    }

    [TestMethod]
    public void ViewerRequestedOff_StaysOffWhenReturningToOriginal()
    {
        var capturer = new Mock<IAudioCapturer>();
        var settings = new MutableStreamSettingsProvider(RemoteStreamSettings.Original);
        using var policy = new AudioPolicyController(capturer.Object, settings);

        policy.SetViewerRequested(true);
        settings.Set(RemoteStreamSettings.Emergency);
        policy.SetViewerRequested(false);
        settings.Set(RemoteStreamSettings.Original);

        capturer.Verify(x => x.ToggleAudio(true), Times.Once);
        capturer.Verify(x => x.ToggleAudio(false), Times.Once);
    }

    private sealed class MutableStreamSettingsProvider : IRemoteStreamSettingsProvider
    {
        public MutableStreamSettingsProvider(RemoteStreamSettings current)
        {
            Current = current;
        }

        public RemoteStreamSettings Current { get; private set; }

        public event EventHandler<RemoteStreamSettings>? SettingsChanged;

        public void Set(RemoteStreamSettings settings)
        {
            Current = settings;
            SettingsChanged?.Invoke(this, settings);
        }
    }
}
