using Microsoft.VisualStudio.TestTools.UnitTesting;
using Remotely.Shared.Dtos;

namespace Remotely.Server.Tests;

[TestClass]
public class AgentRecoveryTests
{
    [TestMethod]
    public void DeviceClientDto_ExposesServerVerificationTokenForRecovery()
    {
        var property = typeof(DeviceClientDto).GetProperty("ServerVerificationToken");

        Assert.IsNotNull(
            property,
            "Resident Agents need to carry their persisted server verification token when the server rebuilds an empty device database.");
        Assert.AreEqual(typeof(string), property!.PropertyType);
    }
}
