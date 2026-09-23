using Microsoft.Extensions.Logging;
using Moq;
using Remotely.Desktop.Shared.Abstractions;
using Remotely.Desktop.Shared.Services;
using Remotely.Shared.Enums;
using Remotely.Shared.Models;
using Remotely.Shared.Services;

namespace Remotely.Desktop.Win.Tests;

[TestClass]
public class ViewerQualityPolicyTests
{
    [TestMethod]
    public async Task EmergencyProfile_SetsQuality45_AndAutoQualityDoesNotClimb()
    {
        var provider = new MutableSettingsProvider(RemoteStreamSettings.Emergency);
        using var viewer = CreateViewer(provider);

        Assert.AreEqual(45, viewer.ImageQuality);

        for (var i = 0; i < 10; i++)
        {
            await viewer.ApplyAutoQuality();
        }

        Assert.AreEqual(45, viewer.ImageQuality);
    }

    [TestMethod]
    public void LiveProfileChange_UpdatesQualityWithoutRecreatingViewer()
    {
        var provider = new MutableSettingsProvider(RemoteStreamSettings.Original);
        using var viewer = CreateViewer(provider);

        provider.Set(RemoteStreamSettings.UltraLow);

        Assert.AreEqual(30, viewer.ImageQuality);
    }

    [TestMethod]
    public async Task ReturningToOriginal_ResumesUpstreamAutoQuality()
    {
        var provider = new MutableSettingsProvider(RemoteStreamSettings.Emergency);
        using var viewer = CreateViewer(provider);
        Assert.AreEqual(45, viewer.ImageQuality);

        provider.Set(RemoteStreamSettings.Original);
        Assert.AreEqual(45, viewer.ImageQuality, "Returning to Original should resume, not jump, upstream auto-quality.");

        await viewer.ApplyAutoQuality();
        Assert.AreEqual(47, viewer.ImageQuality);

        for (var i = 0; i < 30; i++)
        {
            await viewer.ApplyAutoQuality();
        }

        Assert.AreEqual(Viewer.DefaultQuality, viewer.ImageQuality);
    }

    [TestMethod]
    public void OriginalAfterHigherCustom_ClampsToUpstreamCeiling()
    {
        var high = new RemoteStreamSettings(1, RemoteStreamProfile.Custom, 90, 10, RemoteAudioMode.Off);
        var provider = new MutableSettingsProvider(high);
        using var viewer = CreateViewer(provider);
        Assert.AreEqual(90, viewer.ImageQuality);

        provider.Set(RemoteStreamSettings.Original);

        Assert.AreEqual(Viewer.DefaultQuality, viewer.ImageQuality);
    }

    [TestMethod]
    public void Dispose_UnsubscribesFromSettingsProvider()
    {
        var provider = new MutableSettingsProvider(RemoteStreamSettings.Original);
        var viewer = CreateViewer(provider);
        Assert.AreEqual(1, provider.SubscriberCount);

        viewer.Dispose();

        Assert.AreEqual(0, provider.SubscriberCount);
    }

    private static Viewer CreateViewer(IRemoteStreamSettingsProvider provider)
    {
        return new Viewer(
            "tester",
            "viewer-1",
            Mock.Of<IDesktopHubConnection>(),
            Mock.Of<IScreenCapturer>(),
            Mock.Of<IClipboardService>(),
            Mock.Of<IAudioCapturer>(),
            Mock.Of<ISystemTime>(),
            provider,
            Mock.Of<ILogger<Viewer>>());
    }

    private sealed class MutableSettingsProvider : IRemoteStreamSettingsProvider
    {
        private EventHandler<RemoteStreamSettings>? _settingsChanged;

        public MutableSettingsProvider(RemoteStreamSettings initial)
        {
            Current = initial;
        }

        public RemoteStreamSettings Current { get; private set; }
        public int SubscriberCount { get; private set; }

        public event EventHandler<RemoteStreamSettings>? SettingsChanged
        {
            add
            {
                _settingsChanged += value;
                SubscriberCount++;
            }
            remove
            {
                _settingsChanged -= value;
                SubscriberCount--;
            }
        }

        public void Set(RemoteStreamSettings settings)
        {
            Current = settings;
            _settingsChanged?.Invoke(this, settings);
        }
    }
}
