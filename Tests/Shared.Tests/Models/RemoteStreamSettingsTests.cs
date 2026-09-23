using Remotely.Shared.Enums;
using Remotely.Shared.Models;

namespace Remotely.Shared.Tests.Models;

[TestClass]
public class RemoteStreamSettingsTests
{
    [TestMethod]
    public void Presets_MatchApprovedValues()
    {
        Assert.AreEqual(80, RemoteStreamSettings.Original.ImageQuality);
        Assert.IsNull(RemoteStreamSettings.Original.MaxFps);
        Assert.AreEqual(RemoteAudioMode.Original, RemoteStreamSettings.Original.AudioMode);

        Assert.AreEqual(
            new RemoteStreamSettings(1, RemoteStreamProfile.Balanced, 65, 15, RemoteAudioMode.Off),
            RemoteStreamSettings.Balanced);
        Assert.AreEqual(
            new RemoteStreamSettings(1, RemoteStreamProfile.Emergency, 45, 8, RemoteAudioMode.Off),
            RemoteStreamSettings.Emergency);
        Assert.AreEqual(
            new RemoteStreamSettings(1, RemoteStreamProfile.UltraLow, 30, 5, RemoteAudioMode.Off),
            RemoteStreamSettings.UltraLow);
    }

    [TestMethod]
    public void Custom_RejectsOutOfRangeValues()
    {
        Assert.IsFalse(new RemoteStreamSettings(1, RemoteStreamProfile.Custom, 19, 8, RemoteAudioMode.Off).TryValidate(out _));
        Assert.IsFalse(new RemoteStreamSettings(1, RemoteStreamProfile.Custom, 45, 31, RemoteAudioMode.Off).TryValidate(out _));
    }

    [TestMethod]
    public void Original_RejectsChangedUpstreamSemantics()
    {
        Assert.IsFalse(new RemoteStreamSettings(1, RemoteStreamProfile.Original, 79, null, RemoteAudioMode.Original).TryValidate(out _));
        Assert.IsFalse(new RemoteStreamSettings(1, RemoteStreamProfile.Original, 80, 30, RemoteAudioMode.Original).TryValidate(out _));
        Assert.IsFalse(new RemoteStreamSettings(1, RemoteStreamProfile.Original, 80, null, RemoteAudioMode.Off).TryValidate(out _));
    }

    [TestMethod]
    public void SupportedCustomBounds_AreAccepted()
    {
        Assert.IsTrue(new RemoteStreamSettings(1, RemoteStreamProfile.Custom, 20, 2, RemoteAudioMode.Off).TryValidate(out _));
        Assert.IsTrue(new RemoteStreamSettings(1, RemoteStreamProfile.Custom, 90, 30, RemoteAudioMode.Original).TryValidate(out _));
    }
}
