using Remotely.Manager.Win.ViewModels;
using Remotely.Shared.Enums;
using Remotely.Shared.Models;
using Remotely.Shared.Services;

namespace Remotely.Manager.Win.Tests;

[TestClass]
public class SettingsViewModelTests
{
    [TestMethod]
    public void SelectingEmergency_LoadsApprovedPreset()
    {
        var vm = Create(out _);

        vm.SelectPreset(RemoteStreamProfile.Emergency);

        Assert.AreEqual(RemoteStreamProfile.Emergency, vm.Profile);
        Assert.AreEqual(45, vm.ImageQuality);
        Assert.AreEqual(8, vm.MaxFps);
        Assert.IsFalse(vm.EnableAudio);
    }

    [TestMethod]
    public void EditingEmergencyQuality_ChangesProfileToCustom()
    {
        var vm = Create(out _);
        vm.SelectPreset(RemoteStreamProfile.Emergency);

        vm.ImageQuality = 55;

        Assert.AreEqual(RemoteStreamProfile.Custom, vm.Profile);
        Assert.AreEqual(55, vm.ImageQuality);
    }

    [TestMethod]
    public void RestoreDefaults_ReturnsToOriginal()
    {
        var vm = Create(out _);
        vm.SelectPreset(RemoteStreamProfile.UltraLow);

        vm.RestoreDefaults();

        Assert.AreEqual(RemoteStreamProfile.Original, vm.Profile);
        Assert.AreEqual(80, vm.ImageQuality);
        Assert.IsNull(vm.MaxFps);
        Assert.IsTrue(vm.UsesOriginalBehavior);
    }

    [TestMethod]
    public void ManualValues_EnforceApprovedBounds()
    {
        var vm = Create(out _);
        vm.SelectPreset(RemoteStreamProfile.Emergency);

        Assert.ThrowsException<ArgumentOutOfRangeException>(() => vm.ImageQuality = 19);
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => vm.ImageQuality = 91);
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => vm.MaxFps = 1);
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => vm.MaxFps = 31);
    }

    [TestMethod]
    public async Task Save_PersistsCurrentCustomSnapshot()
    {
        var vm = Create(out var store);
        vm.SelectPreset(RemoteStreamProfile.Emergency);
        vm.ImageQuality = 55;
        vm.MaxFps = 10;
        vm.EnableAudio = true;

        await vm.SaveAsync();

        Assert.AreEqual(
            new RemoteStreamSettings(1, RemoteStreamProfile.Custom, 55, 10, RemoteAudioMode.Original),
            store.Saved);
    }

    private static SettingsViewModel Create(out FakeSettingsStore store)
    {
        store = new FakeSettingsStore(RemoteStreamSettings.Original);
        return new SettingsViewModel(store);
    }

    private sealed class FakeSettingsStore : IEmergencySettingsStore
    {
        private readonly RemoteStreamSettings _loaded;

        public FakeSettingsStore(RemoteStreamSettings loaded)
        {
            _loaded = loaded;
        }

        public RemoteStreamSettings? Saved { get; private set; }

        public RemoteStreamSettings Load(out string? warning)
        {
            warning = null;
            return _loaded;
        }

        public Task SaveAsync(RemoteStreamSettings settings, CancellationToken cancellationToken = default)
        {
            Saved = settings;
            return Task.CompletedTask;
        }
    }
}
