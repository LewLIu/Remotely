using System.ServiceProcess;
using Remotely.Manager.Win.Services;

namespace Remotely.Manager.Win.Tests;

[TestClass]
public class ServiceStartupPolicyTests
{
    [TestMethod]
    public void Manual_DoesNotRequireReconfiguration()
    {
        Assert.IsFalse(ServiceStartupPolicy.RequiresManualConfiguration(ServiceStartMode.Manual));
    }

    [DataTestMethod]
    [DataRow(ServiceStartMode.Automatic)]
    [DataRow(ServiceStartMode.Disabled)]
    [DataRow(ServiceStartMode.Boot)]
    [DataRow(ServiceStartMode.System)]
    public void NonManual_RequiresReconfiguration(ServiceStartMode mode)
    {
        Assert.IsTrue(ServiceStartupPolicy.RequiresManualConfiguration(mode));
    }
}
