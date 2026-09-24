using Remotely.Manager.Win.Services;

namespace Remotely.Manager.Win.Tests;

[TestClass]
public class ManagerCoordinatorTests
{
    [TestMethod]
    public async Task Initialize_WhenStopped_StartsService()
    {
        var fake = new FakeServiceController(RemotelyServiceState.Stopped);
        var coordinator = new ManagerCoordinator(fake);

        await coordinator.InitializeAsync();

        Assert.AreEqual(1, fake.StartCalls);
        Assert.AreEqual(RemotelyServiceState.Running, coordinator.CurrentState);
    }

    [TestMethod]
    public async Task Initialize_WhenRunning_DoesNotStartAgain()
    {
        var fake = new FakeServiceController(RemotelyServiceState.Running);
        var coordinator = new ManagerCoordinator(fake);

        await coordinator.InitializeAsync();

        Assert.AreEqual(0, fake.StartCalls);
        Assert.AreEqual(RemotelyServiceState.Running, coordinator.CurrentState);
    }

    [TestMethod]
    public async Task MissingService_IsReportedAsError()
    {
        var fake = new FakeServiceController(RemotelyServiceState.Missing);
        var coordinator = new ManagerCoordinator(fake);

        await coordinator.InitializeAsync();

        Assert.AreEqual(RemotelyServiceState.Error, coordinator.CurrentState);
        StringAssert.Contains(coordinator.LastError ?? string.Empty, "not installed");
    }

    [TestMethod]
    public async Task Refresh_ReflectsExternalServiceChange()
    {
        var fake = new FakeServiceController(RemotelyServiceState.Running);
        var coordinator = new ManagerCoordinator(fake);
        await coordinator.InitializeAsync();

        fake.State = RemotelyServiceState.Stopped;
        await coordinator.RefreshAsync();

        Assert.AreEqual(RemotelyServiceState.Stopped, coordinator.CurrentState);
    }

    [TestMethod]
    public async Task Exit_WhenRunning_StopsAndConfirmsStopped()
    {
        var fake = new FakeServiceController(RemotelyServiceState.Running);
        var coordinator = new ManagerCoordinator(fake);

        var result = await coordinator.TryExitAsync(TimeSpan.FromSeconds(1));

        Assert.IsTrue(result);
        Assert.AreEqual(1, fake.StopCalls);
        Assert.AreEqual(RemotelyServiceState.Stopped, coordinator.CurrentState);
    }

    [TestMethod]
    public async Task Exit_StopTimeout_ReturnsFalseAndKeepsErrorVisible()
    {
        var fake = new FakeServiceController(RemotelyServiceState.Running)
        {
            StopException = new TimeoutException("stop timed out")
        };
        var coordinator = new ManagerCoordinator(fake);

        var result = await coordinator.TryExitAsync(TimeSpan.FromMilliseconds(50));

        Assert.IsFalse(result);
        Assert.AreEqual(RemotelyServiceState.Error, coordinator.CurrentState);
        StringAssert.Contains(coordinator.LastError ?? string.Empty, "stop timed out");
    }

    private sealed class FakeServiceController : IRemotelyServiceController
    {
        public FakeServiceController(RemotelyServiceState state)
        {
            State = state;
        }

        public RemotelyServiceState State { get; set; }
        public Exception? StartException { get; set; }
        public Exception? StopException { get; set; }
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }

        public Task<RemotelyServiceState> GetStateAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(State);

        public Task StartAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            StartCalls++;
            if (StartException is not null) throw StartException;
            State = RemotelyServiceState.Running;
            return Task.CompletedTask;
        }

        public Task StopAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            StopCalls++;
            if (StopException is not null) throw StopException;
            State = RemotelyServiceState.Stopped;
            return Task.CompletedTask;
        }
    }
}
