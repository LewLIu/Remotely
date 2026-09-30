using Microsoft.VisualStudio.TestTools.UnitTesting;
using Remotely.Server.Services;
using System.Threading.Tasks;

namespace Remotely.Server.Tests;

[TestClass]
public class BootstrapServiceTests
{
    [TestMethod]
    public void BootstrapServiceTypeExists()
    {
        var bootstrapType = typeof(DataService).Assembly.GetType("Remotely.Server.Services.BootstrapService");

        Assert.IsNotNull(bootstrapType, "BootstrapService should exist before bootstrap behavior can be tested.");
    }

    [TestMethod]
    public void BootstrapServiceExposesEnsureBootstrapAdminAsync()
    {
        var method = typeof(BootstrapService).GetMethod("EnsureBootstrapAdminAsync");

        Assert.IsNotNull(method, "BootstrapService should expose EnsureBootstrapAdminAsync for startup integration.");
        Assert.AreEqual(typeof(Task), method.ReturnType);
    }
}
