using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Remotely.Server.Data;
using Remotely.Shared.Entities;

namespace Remotely.Server.Services;

public sealed class BootstrapService(
    AppDb appDb,
    UserManager<RemotelyUser> userManager,
    IConfiguration configuration,
    ILogger<BootstrapService> logger)
{
    private const string EmailKey = "BOOTSTRAP_EMAIL";
    private const string PasswordKey = "BOOTSTRAP_PASSWORD";
    private const string OrganizationIdKey = "BOOTSTRAP_ORG_ID";

    public async Task EnsureBootstrapAdminAsync()
    {
        if (await appDb.Organizations.AnyAsync())
        {
            logger.LogDebug("Skipping bootstrap because an organization already exists.");
            return;
        }

        var email = configuration[EmailKey]?.Trim();
        var password = configuration[PasswordKey];
        var organizationId = configuration[OrganizationIdKey]?.Trim();

        var hasEmail = !string.IsNullOrWhiteSpace(email);
        var hasPassword = !string.IsNullOrWhiteSpace(password);
        var hasOrganizationId = !string.IsNullOrWhiteSpace(organizationId);

        if (!hasEmail && !hasPassword && !hasOrganizationId)
        {
            logger.LogDebug("Bootstrap configuration is absent; preserving normal registration behavior.");
            return;
        }

        if (!hasEmail || !hasPassword || !hasOrganizationId)
        {
            throw new InvalidOperationException(
                "CloudBase bootstrap requires REMOTELY_BOOTSTRAP_EMAIL, " +
                "REMOTELY_BOOTSTRAP_PASSWORD, and REMOTELY_BOOTSTRAP_ORG_ID to all be set.");
        }

        var organization = new Organization
        {
            ID = organizationId!,
            OrganizationName = string.Empty,
            IsDefaultOrganization = true
        };

        var user = new RemotelyUser
        {
            UserName = email!,
            Email = email!,
            EmailConfirmed = true,
            IsServerAdmin = true,
            IsAdministrator = true,
            LockoutEnabled = true,
            Organization = organization,
            OrganizationID = organizationId!,
            UserOptions = new RemotelyUserOptions()
        };

        var result = await userManager.CreateAsync(user, password!);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(x => x.Description));
            throw new InvalidOperationException($"Failed to create bootstrap administrator: {errors}");
        }

        logger.LogInformation(
            "Created bootstrap administrator {Email} with fixed organization {OrganizationId}.",
            email,
            organizationId);
    }
}
