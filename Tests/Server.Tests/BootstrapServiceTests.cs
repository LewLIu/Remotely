using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Remotely.Server.Data;
using Remotely.Server.Services;
using Remotely.Shared.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Remotely.Server.Tests;

[TestClass]
[DoNotParallelize]
public class BootstrapServiceTests
{
    private const string BootstrapEmail = "bootstrap@example.test";
    private const string BootstrapPassword = "BootstrapPass123";
    private const string BootstrapOrgId = "fixed-cloudbase-org";

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

    [TestMethod]
    public async Task EmptyDatabaseWithCompleteConfigCreatesFixedAdminAndOrganization()
    {
        await using var provider = CreateProvider(CreateCompleteSettings());
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        await ResetDatabase(db);

        var service = scope.ServiceProvider.GetRequiredService<BootstrapService>();
        await service.EnsureBootstrapAdminAsync();

        Assert.AreEqual(1, await db.Organizations.CountAsync());
        var organization = await db.Organizations.SingleAsync();
        Assert.AreEqual(BootstrapOrgId, organization.ID);
        Assert.IsTrue(organization.IsDefaultOrganization);

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<RemotelyUser>>();
        var user = await userManager.FindByEmailAsync(BootstrapEmail);
        Assert.IsNotNull(user);
        Assert.AreEqual(BootstrapOrgId, user.OrganizationID);
        Assert.IsTrue(user.IsServerAdmin);
        Assert.IsTrue(user.IsAdministrator);
        Assert.IsTrue(user.EmailConfirmed);
        Assert.IsFalse(string.IsNullOrWhiteSpace(user.PasswordHash));
        Assert.AreNotEqual(BootstrapPassword, user.PasswordHash);
        Assert.IsTrue(await userManager.CheckPasswordAsync(user, BootstrapPassword));
    }

    [TestMethod]
    public async Task EmptyDatabaseWithoutBootstrapConfigIsNoOp()
    {
        await using var provider = CreateProvider(new Dictionary<string, string?>());
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        await ResetDatabase(db);

        var service = scope.ServiceProvider.GetRequiredService<BootstrapService>();
        await service.EnsureBootstrapAdminAsync();

        Assert.AreEqual(0, await db.Organizations.CountAsync());
        Assert.AreEqual(0, await db.Users.CountAsync());
    }

    [TestMethod]
    public async Task EmptyDatabaseWithPartialBootstrapConfigFailsClearly()
    {
        var settings = new Dictionary<string, string?>
        {
            ["BOOTSTRAP_EMAIL"] = BootstrapEmail
        };

        await using var provider = CreateProvider(settings);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        await ResetDatabase(db);

        var service = scope.ServiceProvider.GetRequiredService<BootstrapService>();
        var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => service.EnsureBootstrapAdminAsync());

        StringAssert.Contains(exception.Message, "REMOTELY_BOOTSTRAP_EMAIL");
        StringAssert.Contains(exception.Message, "REMOTELY_BOOTSTRAP_PASSWORD");
        StringAssert.Contains(exception.Message, "REMOTELY_BOOTSTRAP_ORG_ID");
        Assert.AreEqual(0, await db.Organizations.CountAsync());
    }

    [TestMethod]
    public async Task ExistingOrganizationIsNeverOverwritten()
    {
        await using var provider = CreateProvider(CreateCompleteSettings());
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        await ResetDatabase(db);

        db.Organizations.Add(new Organization
        {
            ID = "existing-org",
            OrganizationName = "Existing",
            IsDefaultOrganization = true
        });
        await db.SaveChangesAsync();

        var service = scope.ServiceProvider.GetRequiredService<BootstrapService>();
        await service.EnsureBootstrapAdminAsync();

        Assert.AreEqual(1, await db.Organizations.CountAsync());
        Assert.IsNotNull(await db.Organizations.FindAsync("existing-org"));
        Assert.IsNull(await db.Organizations.FindAsync(BootstrapOrgId));

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<RemotelyUser>>();
        Assert.IsNull(await userManager.FindByEmailAsync(BootstrapEmail));
    }

    private static Dictionary<string, string?> CreateCompleteSettings()
    {
        return new Dictionary<string, string?>
        {
            ["BOOTSTRAP_EMAIL"] = BootstrapEmail,
            ["BOOTSTRAP_PASSWORD"] = BootstrapPassword,
            ["BOOTSTRAP_ORG_ID"] = BootstrapOrgId
        };
    }

    private static ServiceProvider CreateProvider(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(x => x.EnvironmentName).Returns(Environments.Production);

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(environment.Object);
        services.AddLogging();
        services.AddDbContext<AppDb, TestingDbContext>(
            contextLifetime: ServiceLifetime.Scoped,
            optionsLifetime: ServiceLifetime.Scoped);
        services
            .AddIdentity<RemotelyUser, IdentityRole>(options =>
            {
                options.Stores.MaxLengthForKeys = 128;
                options.Password.RequireNonAlphanumeric = false;
            })
            .AddEntityFrameworkStores<AppDb>()
            .AddDefaultTokenProviders();
        services.AddScoped<BootstrapService>();

        return services.BuildServiceProvider();
    }

    private static async Task ResetDatabase(AppDb db)
    {
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }
}
