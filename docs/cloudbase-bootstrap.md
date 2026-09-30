# CloudBase Bootstrap for Ephemeral SQLite

This deployment mode is intended for small, emergency-access Remotely installations where the CloudBase container filesystem may be recreated and preserving the SQLite database is not required.

The goal is narrower than full database persistence: after an empty database is recreated, the server restores the same administrator identity and the same organization ID so previously installed Resident Agents can reconnect to the same organization and repopulate device records.

## Required environment variables

Set all three variables in the CloudBase container environment:

```text
REMOTELY_BOOTSTRAP_EMAIL=<administrator email>
REMOTELY_BOOTSTRAP_PASSWORD=<administrator password>
REMOTELY_BOOTSTRAP_ORG_ID=<stable organization id>
```

Do not commit real values to Git, Dockerfiles, ZIP packages, install scripts, or checked-in configuration files.

Remotely loads environment variables with the `Remotely_` prefix, so these variables are exposed internally as `BOOTSTRAP_EMAIL`, `BOOTSTRAP_PASSWORD`, and `BOOTSTRAP_ORG_ID`.

## Choosing the organization ID

The organization ID is the compatibility key for already-installed Resident Agents.

- If Resident Agents are already installed, set `REMOTELY_BOOTSTRAP_ORG_ID` to the same organization ID they currently use.
- For a brand-new installation, generate one stable unique ID (for example, a UUID) and keep it unchanged for future CloudBase rebuilds.
- Do not rotate this ID casually. Changing it breaks the continuity this bootstrap mechanism is designed to preserve.

## Startup behavior

At server startup, after database migrations and after ASP.NET Identity is configured:

1. If at least one `Organization` already exists, bootstrap is skipped and the existing database is left untouched.
2. If the database has no organizations and all three bootstrap variables are absent, Remotely keeps its normal registration behavior.
3. If the database has no organizations and only some bootstrap variables are configured, startup fails with a clear configuration error instead of creating a partial or inconsistent identity.
4. If the database has no organizations and all three variables are configured, Remotely creates:
   - one default organization using the configured fixed organization ID;
   - one confirmed administrator using the configured email;
   - server-admin and organization-admin privileges for that user.
5. The password is passed through ASP.NET Identity `UserManager.CreateAsync`, so the database stores the normal Identity password hash rather than the plaintext password.

## What this restores after an ephemeral database reset

After CloudBase recreates an empty SQLite database and the container starts again, bootstrap restores the same login email and organization ID. Resident Agents that still hold that organization ID can reconnect and recreate their device records through the normal server device-online/update flow.

This is not full persistence. Data that exists only in SQLite can still be lost, including items such as device metadata/nicknames, historical records, server-side settings, and other user-created data that is not reconstructed by an Agent reconnect.

## Safety properties

- Existing databases are never overwritten by bootstrap.
- No default password exists in source code.
- No password is logged by the bootstrap service.
- Missing configuration does not silently create an insecure default account.
- Partial configuration fails closed rather than guessing values.

## Deployment checklist

Before enabling bootstrap in CloudBase:

1. Confirm the organization ID used by the currently installed Resident Agents, or generate a new stable ID for a new installation.
2. Set all three `REMOTELY_BOOTSTRAP_*` environment variables in CloudBase.
3. Redeploy/restart the container.
4. Verify the configured administrator can sign in.
5. Verify existing Resident Agents reconnect and the expected devices reappear.
6. Test one deliberate empty-database rebuild before relying on the setup for emergency access.
