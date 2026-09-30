using Remotely.Shared.Enums;
using Remotely.Shared.Models;

namespace Remotely.Shared.Tests.Models;

[TestClass]
public class RemoteStreamSettingsTests
{
    [TestMethod]
    public void Presets_MatchApprovedV2Values()
    {
        Assert.AreEqual(80, RemoteStreamSettings.Original.ImageQuality);
        Assert.IsNull(RemoteStreamSettings.Original.MaxFps);
        Assert.IsNull(RemoteStreamSettings.Original.MaxStreamWidth);
        Assert.IsNull(RemoteStreamSettings.Original.MaxStreamHeight);
        Assert.AreEqual(RemoteAudioMode.Original, RemoteStreamSettings.Original.AudioMode);

        Assert.AreEqual(
            new RemoteStreamSettings(1, RemoteStreamProfile.Balanced, 55, 20, RemoteAudioMode.Off, 1600, 900),
            RemoteStreamSettings.Balanced);
        Assert.AreEqual(
            new RemoteStreamSettings(1, RemoteStreamProfile.Emergency, 35, 20, RemoteAudioMode.Off, 1280, 720),
            RemoteStreamSettings.Emergency);
        Assert.AreEqual(
            new RemoteStreamSettings(1, RemoteStreamProfile.UltraLow, 20, 20, RemoteAudioMode.Off, 960, 540),
            RemoteStreamSettings.UltraLow);
    }

    [TestMethod]
    public void Custom_RejectsOutOfRangeValues()
    {
        Assert.IsFalse(new RemoteStreamSettings(1, RemoteStreamProfile.Custom, 19, 8, RemoteAudioMode.Off).TryValidate(out _));
        Assert.IsFalse(new RemoteStreamSettings(1, RemoteStreamProfile.Custom, 45, 31, RemoteAudioMode.Off).TryValidate(out _));
        Assert.IsFalse(new RemoteStreamSettings(1, RemoteStreamProfile.Custom, 45, 20, RemoteAudioMode.Off, 639, 360).TryValidate(out _));
        Assert.IsFalse(new RemoteStreamSettings(1, RemoteStreamProfile.Custom, 45, 20, RemoteAudioMode.Off, 640, null).TryValidate(out _));
    }

    [TestMethod]
    public void Original_RejectsChangedUpstreamSemantics()
    {
        Assert.IsFalse(new RemoteStreamSettings(1, RemoteStreamProfile.Original, 79, null, RemoteAudioMode.Original).TryValidate(out _));
        Assert.IsFalse(new RemoteStreamSettings(1, RemoteStreamProfile.Original, 80, 30, RemoteAudioMode.Original).TryValidate(out _));
        Assert.IsFalse(new RemoteStreamSettings(1, RemoteStreamProfile.Original, 80, null, RemoteAudioMode.Off).TryValidate(out _));
        Assert.IsFalse(new RemoteStreamSettings(1, RemoteStreamProfile.Original, 80, null, RemoteAudioMode.Original, 1280, 720).TryValidate(out _));
    }

    [TestMethod]
    public void SupportedCustomBounds_AreAccepted()
    {
        Assert.IsTrue(new RemoteStreamSettings(1, RemoteStreamProfile.Custom, 20, 2, RemoteAudioMode.Off, 640, 360).TryValidate(out _));
        Assert.IsTrue(new RemoteStreamSettings(1, RemoteStreamProfile.Custom, 90, 30, RemoteAudioMode.Original, 1600, 900).TryValidate(out _));
        Assert.IsTrue(new RemoteStreamSettings(1, RemoteStreamProfile.Custom, 90, 30, RemoteAudioMode.Original).TryValidate(out _));
    }
}
