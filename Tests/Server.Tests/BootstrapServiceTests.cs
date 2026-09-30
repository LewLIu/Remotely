using Microsoft.VisualStudio.TestTools.UnitTesting;
using Remotely.Server.Services;

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
}
