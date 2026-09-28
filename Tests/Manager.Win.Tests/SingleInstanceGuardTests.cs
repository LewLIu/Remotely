using Remotely.Manager.Win.Services;

namespace Remotely.Manager.Win.Tests;

[TestClass]
public class SingleInstanceGuardTests
{
    [TestMethod]
    public void OnlyOneGuardOwnsNamedMutexAtATime()
    {
        var mutexName = $"Local\\Remotely_Manager_Test_{Guid.NewGuid():N}";

        using (var first = new SingleInstanceGuard(mutexName))
        {
            Assert.IsTrue(first.IsPrimaryInstance);

            using var second = new SingleInstanceGuard(mutexName);
            Assert.IsFalse(second.IsPrimaryInstance);
        }

        using var third = new SingleInstanceGuard(mutexName);
        Assert.IsTrue(third.IsPrimaryInstance);
    }
}
