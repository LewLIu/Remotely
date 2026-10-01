using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Remotely.Server.Services;
using Remotely.Shared.Dtos;
using System;
using System.Threading.Tasks;

namespace Remotely.Server.Tests;

[TestClass]
public class AgentRecoveryTests
{
    private IDataService _dataService = null!;
    private TestData _testData = null!;

    [TestMethod]
    public void DeviceClientDto_ExposesServerVerificationTokenForRecovery()
    {
        var property = typeof(DeviceClientDto).GetProperty("ServerVerificationToken");

        Assert.IsNotNull(
            property,
            "Resident Agents need to carry their persisted server verification token when the server rebuilds an empty device database.");
        Assert.AreEqual(typeof(string), property!.PropertyType);
    }

    [TestMethod]
    public async Task AddOrUpdateDevice_WhenDeviceIsNew_SeedsPersistedVerificationToken()
    {
        var deviceId = Guid.NewGuid().ToString();
        const string persistedToken = "persisted-agent-token";

        var result = await _dataService.AddOrUpdateDevice(new DeviceClientDto
        {
            ID = deviceId,
            OrganizationID = _testData.Org1Id,
            DeviceName = "Recovered Device",
            ServerVerificationToken = persistedToken
        });

        Assert.IsTrue(result.IsSuccess);

        var storedDevice = (await _dataService.GetDevice(deviceId)).Value;
        Assert.IsNotNull(storedDevice);
        Assert.AreEqual(persistedToken, storedDevice!.ServerVerificationToken);
    }

    [TestMethod]
    public async Task AddOrUpdateDevice_WhenDeviceExists_DoesNotReplaceVerificationToken()
    {
        const string serverToken = "trusted-server-token";
        const string conflictingAgentToken = "different-agent-token";
        var existingDevice = _testData.Org1Device1;

        _dataService.SetServerVerificationToken(existingDevice.ID, serverToken);

        var result = await _dataService.AddOrUpdateDevice(new DeviceClientDto
        {
            ID = existingDevice.ID,
            OrganizationID = existingDevice.OrganizationID,
            DeviceName = existingDevice.DeviceName ?? "Existing Device",
            ServerVerificationToken = conflictingAgentToken
        });

        Assert.IsTrue(result.IsSuccess);

        var storedDevice = (await _dataService.GetDevice(existingDevice.ID)).Value;
        Assert.IsNotNull(storedDevice);
        Assert.AreEqual(serverToken, storedDevice!.ServerVerificationToken);
    }

    [TestInitialize]
    public async Task TestInitialize()
    {
        _testData = new TestData();
        await _testData.Init();
        _dataService = IoCActivator.ServiceProvider.GetRequiredService<IDataService>();
    }

    [TestCleanup]
    public void TestCleanup()
    {
        _testData.ClearData();
    }
}
