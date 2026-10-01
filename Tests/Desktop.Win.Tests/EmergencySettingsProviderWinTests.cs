using System.Collections.Concurrent;
using System.Diagnostics;
using Remotely.Desktop.Win.Services;
using Remotely.Shared.Models;
using Remotely.Shared.Services;

namespace Remotely.Desktop.Win.Tests;

[TestClass]
public class EmergencySettingsProviderWinTests
{
    private string _directory = null!;
    private string _path = null!;

    [TestInitialize]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "Remotely.Desktop.Win.Tests", Guid.NewGuid().ToString("N"));
        _path = Path.Combine(_directory, "EmergencySettings.json");
        Directory.CreateDirectory(_directory);
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
    public void MissingFile_StartsWithOriginal()
    {
        using var provider = new EmergencySettingsProviderWin(_path, TimeSpan.FromMilliseconds(50));

        Assert.AreEqual(RemoteStreamSettings.Original, provider.Current);
    }

    [TestMethod]
    public async Task AtomicSave_AppliesToActiveProviderWithinOneSecond()
    {
        using var provider = new EmergencySettingsProviderWin(_path, TimeSpan.FromMilliseconds(50));
        var store = new EmergencySettingsStore(_path);
        var changed = new TaskCompletionSource<RemoteStreamSettings>(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.SettingsChanged += (_, settings) => changed.TrySetResult(settings);
        var stopwatch = Stopwatch.StartNew();

        await store.SaveAsync(RemoteStreamSettings.Emergency);

        var completed = await Task.WhenAny(changed.Task, Task.Delay(TimeSpan.FromSeconds(1)));
        Assert.AreSame(changed.Task, completed, "Settings did not propagate within one second.");
        Assert.AreEqual(RemoteStreamSettings.Emergency, await changed.Task);
        Assert.AreEqual(RemoteStreamSettings.Emergency, provider.Current);
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
    }

    [TestMethod]
    public async Task CorruptRewrite_AfterGoodLoad_KeepsLastKnownGood()
    {
        var store = new EmergencySettingsStore(_path);
        await store.SaveAsync(RemoteStreamSettings.Balanced);
        using var provider = new EmergencySettingsProviderWin(_path, TimeSpan.FromMilliseconds(50));
        var observed = new ConcurrentQueue<RemoteStreamSettings>();
        provider.SettingsChanged += (_, settings) => observed.Enqueue(settings);

        await File.WriteAllTextAsync(_path, "{not-json");
        await Task.Delay(300);

        Assert.AreEqual(RemoteStreamSettings.Balanced, provider.Current);
        Assert.IsFalse(observed.Any(x => x != RemoteStreamSettings.Balanced));
    }

    [TestMethod]
    public async Task DeleteSettingsFile_RevertsToOriginal()
    {
        var store = new EmergencySettingsStore(_path);
        await store.SaveAsync(RemoteStreamSettings.Emergency);
        using var provider = new EmergencySettingsProviderWin(_path, TimeSpan.FromMilliseconds(50));
        var changed = new TaskCompletionSource<RemoteStreamSettings>(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.SettingsChanged += (_, settings) =>
        {
            if (settings == RemoteStreamSettings.Original)
            {
                changed.TrySetResult(settings);
            }
        };

        File.Delete(_path);

        var completed = await Task.WhenAny(changed.Task, Task.Delay(TimeSpan.FromSeconds(1)));
        Assert.AreSame(changed.Task, completed);
        Assert.AreEqual(RemoteStreamSettings.Original, provider.Current);
    }

    [TestMethod]
    public async Task RapidAtomicWrites_ExposeOnlyValidSnapshots_AndEndAtLatest()
    {
        using var provider = new EmergencySettingsProviderWin(_path, TimeSpan.FromMilliseconds(75));
        var store = new EmergencySettingsStore(_path);
        var observed = new ConcurrentQueue<RemoteStreamSettings>();
        provider.SettingsChanged += (_, settings) => observed.Enqueue(settings);

        await store.SaveAsync(RemoteStreamSettings.Balanced);
        await store.SaveAsync(RemoteStreamSettings.Emergency);
        await store.SaveAsync(RemoteStreamSettings.UltraLow);

        await WaitUntilAsync(
            () => provider.Current == RemoteStreamSettings.UltraLow,
            TimeSpan.FromSeconds(1));

        Assert.IsTrue(observed.Count >= 1);
        Assert.IsTrue(observed.All(settings => settings.TryValidate(out _)));
        Assert.AreEqual(RemoteStreamSettings.UltraLow, provider.Current);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed >= timeout)
            {
                Assert.Fail($"Condition did not become true within {timeout}.");
            }

            await Task.Delay(20);
        }
    }
}
