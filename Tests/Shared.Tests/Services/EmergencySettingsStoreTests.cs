using System.Text.Json;
using Remotely.Shared.Enums;
using Remotely.Shared.Models;
using Remotely.Shared.Services;

namespace Remotely.Shared.Tests.Services;

[TestClass]
public class EmergencySettingsStoreTests
{
    private string _directory = null!;
    private string _path = null!;

    [TestInitialize]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "Remotely.Shared.Tests", Guid.NewGuid().ToString("N"));
        _path = Path.Combine(_directory, "EmergencySettings.json");
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    [TestMethod]
    public void MissingFile_ReturnsOriginalWithoutWarning()
    {
        var store = new EmergencySettingsStore(_path);

        var settings = store.Load(out var warning);

        Assert.AreEqual(RemoteStreamSettings.Original, settings);
        Assert.IsNull(warning);
    }

    [TestMethod]
    public void CorruptJson_ReturnsOriginalWithWarning()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_path, "{not-json");
        var store = new EmergencySettingsStore(_path);

        var settings = store.Load(out var warning);

        Assert.AreEqual(RemoteStreamSettings.Original, settings);
        Assert.IsFalse(string.IsNullOrWhiteSpace(warning));
    }

    [TestMethod]
    public void UnknownFields_AreTolerated()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_path, """
        {
          "version": 1,
          "profile": "Emergency",
          "imageQuality": 45,
          "maxFps": 8,
          "audioMode": "Off",
          "futureField": true
        }
        """);
        var store = new EmergencySettingsStore(_path);

        var settings = store.Load(out var warning);

        Assert.AreEqual(RemoteStreamSettings.Emergency, settings);
        Assert.IsNull(warning);
    }

    [TestMethod]
    public async Task ValidCustom_RoundTrips()
    {
        var store = new EmergencySettingsStore(_path);
        var expected = new RemoteStreamSettings(1, RemoteStreamProfile.Custom, 52, 9, RemoteAudioMode.Off);

        await store.SaveAsync(expected, CancellationToken.None);
        var actual = store.Load(out var warning);

        Assert.AreEqual(expected, actual);
        Assert.IsNull(warning);
    }

    [TestMethod]
    public async Task InvalidSave_IsRefusedAndKeepsLastValidDestination()
    {
        var store = new EmergencySettingsStore(_path);
        await store.SaveAsync(RemoteStreamSettings.Balanced, CancellationToken.None);
        var invalid = new RemoteStreamSettings(1, RemoteStreamProfile.Custom, 10, 8, RemoteAudioMode.Off);

        await Assert.ThrowsExceptionAsync<ArgumentException>(() => store.SaveAsync(invalid, CancellationToken.None));

        var actual = store.Load(out _);
        Assert.AreEqual(RemoteStreamSettings.Balanced, actual);
        Assert.IsFalse(File.Exists(_path + ".tmp"));
    }

    [TestMethod]
    public void InvalidPersistedValues_ReturnOriginalWithWarning()
    {
        Directory.CreateDirectory(_directory);
        var invalid = new
        {
            version = 1,
            profile = "Custom",
            imageQuality = 99,
            maxFps = 8,
            audioMode = "Off"
        };
        File.WriteAllText(_path, JsonSerializer.Serialize(invalid));
        var store = new EmergencySettingsStore(_path);

        var settings = store.Load(out var warning);

        Assert.AreEqual(RemoteStreamSettings.Original, settings);
        Assert.IsFalse(string.IsNullOrWhiteSpace(warning));
    }
}
