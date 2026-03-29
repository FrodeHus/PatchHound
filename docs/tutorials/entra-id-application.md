# Create the Entra ID Application

PatchHound needs Microsoft Entra ID for two separate concerns:

- User authentication into the PatchHound web application
- App-only access for tenant lookup and, optionally, Microsoft Defender ingestion

This guide creates a single Entra application that can handle sign-in and tenant lookup. You can also reuse it for Defender ingestion if that fits your security model, but many teams prefer a separate app registration for Defender.

## What PatchHound Expects

PatchHound uses these values at runtime:

- `AZURE_AD_CLIENT_ID`
- `AZURE_AD_TENANT_ID`
- `AZURE_AD_AUDIENCE`
- `ENTRA_CLIENT_SECRET`
- `FRONTEND_ORIGIN`

In Docker, the frontend derives these values automatically:

- `ENTRA_CLIENT_ID` = `AZURE_AD_CLIENT_ID`
- `ENTRA_TENANT_ID` = `AZURE_AD_TENANT_ID`
- `ENTRA_REDIRECT_URI` = `${FRONTEND_ORIGIN}/auth/callback`

## 1. Register the Application

1. Open the Microsoft Entra admin center.
2. Go to `Identity > Applications > App registrations`.
3. Select `New registration`.
4. Give the application a name such as `PatchHound`.
5. Choose the supported account type:
   - Use `Accounts in this organizational directory only` for a single-tenant deployment.
   - Use `Accounts in any organizational directory` if you want multi-tenant sign-in.
6. Add a Web redirect URI:
   - Local Docker/dev example: `http://localhost:3000/auth/callback`
   - Replace the hostname for your real deployment later.
7. Create the app registration.

## 2. Record the Core Values

From the app overview page, record:

- `Application (client) ID` -> `AZURE_AD_CLIENT_ID`
- `Directory (tenant) ID` -> `AZURE_AD_TENANT_ID`

For `AZURE_AD_AUDIENCE`, use the same application client ID unless you have configured a custom application ID URI and are intentionally validating that instead.

Recommended local example:

```env
AZURE_AD_CLIENT_ID=<application-client-id>
AZURE_AD_TENANT_ID=common
AZURE_AD_AUDIENCE=<application-client-id>
ENTRA_CLIENT_SECRET=<generated-client-secret>
FRONTEND_ORIGIN=http://localhost:3000
```

Notes:

- `AZURE_AD_TENANT_ID=common` is useful for multi-tenant sign-in.
- For a single-tenant deployment, set `AZURE_AD_TENANT_ID` to the tenant GUID instead.

## 3. Create a Client Secret

1. Open `Certificates & secrets`.
2. Create a new client secret.
3. Copy the secret value immediately.
4. Store it as `ENTRA_CLIENT_SECRET`.

PatchHound uses this secret for:

- Frontend server-side auth flows with MSAL
- Microsoft Graph app-only lookup of tenant display name during setup
- Defender ingestion as well, if you choose to reuse the same app for that purpose

## 4. Configure Redirect URIs

Under `Authentication`:

- Keep the Web redirect URI for every PatchHound origin you will use.
- Local example: `http://localhost:3000/auth/callback`
- Production example: `https://patchhound.example.com/auth/callback`

PatchHound also logs users out through Entra, so keep the public application origin stable and consistent with `FRONTEND_ORIGIN`.

## 5. Add the Required PatchHound App Role

PatchHound setup requires the signed-in user to have the `Tenant.Admin` app role.

To add it:

1. Open `App roles` for the app registration.
2. Create a new app role with these values:

```text
Display name: Tenant Admin
Allowed member types: Users/Groups
Value: Tenant.Admin
Description: Allows the user to initialize and administer a PatchHound tenant
Do you want to enable this app role?: Yes
```

3. Save the role.

Then assign it:

1. Go to `Enterprise applications`.
2. Open the service principal for your PatchHound app.
3. Go to `Users and groups`.
4. Assign the `Tenant Admin` role to the users or groups who should be allowed to complete setup.

Important:

- The setup flow checks for the exact role value `Tenant.Admin`.
- After assigning the role, the user should sign out and sign back in before retrying setup.

## 6. API Permissions for Sign-In

For normal login, PatchHound requests standard OpenID Connect scopes:

- `openid`
- `profile`
- `email`
- `offline_access`

These are standard delegated permissions and are usually present automatically for sign-in scenarios.

## 7. Required API Permissions

Configure these API permissions on the Entra application.

### Microsoft Graph

Add this delegated permission:

- `User.Read`

This covers user sign-in and basic profile access.

### WindowsDefenderATP

Add these application permissions:

- `Machine.Read.All`
- `Score.Read.All`
- `Software.Read.All`
- `Vulnerability.Read.All`

Then grant admin consent for the tenant.

These are the permissions PatchHound expects for Defender-based data retrieval.

## 8. Defender Ingestion Notes

PatchHound’s built-in Defender source uses:

- API base URL: `https://api.securitycenter.microsoft.com`
- Token scope: `https://api.securitycenter.microsoft.com/.default`

If you want to reuse the same Entra application for Defender ingestion:

1. Add the required `WindowsDefenderATP` application permissions listed above.
2. Grant admin consent.
3. Use the same tenant ID, client ID, and client secret when configuring the Defender source inside PatchHound.

If you prefer separation of duties, create a second app registration just for Defender and use that in the tenant source configuration instead. That is usually the cleaner production setup.

## 9. Update PatchHound Configuration

Set the auth values in `.env`:

```env
AZURE_AD_CLIENT_ID=<application-client-id>
AZURE_AD_TENANT_ID=<tenant-id-or-common>
AZURE_AD_AUDIENCE=<application-client-id>
ENTRA_CLIENT_SECRET=<client-secret>
FRONTEND_ORIGIN=http://localhost:3000
```

Docker Compose maps these into the API and frontend automatically.

## 10. Validate the Setup

After restarting PatchHound:

1. Open the frontend.
2. Sign in with a user assigned to the `Tenant.Admin` app role.
3. Confirm that setup can proceed without the `Tenant.Admin is required` error.
4. Complete initial tenant setup.
5. If you configured Defender credentials, test the source from the admin area.

## Troubleshooting

`AADSTS50011` or redirect mismatch:

- The Entra app redirect URI does not match `${FRONTEND_ORIGIN}/auth/callback`.

Login succeeds but PatchHound setup is blocked:

- The user is missing the `Tenant.Admin` app role.
- The role was assigned after the user signed in and the session needs to be refreshed.

API authentication fails with audience errors:

- `AZURE_AD_AUDIENCE` does not match the token audience PatchHound is validating.
- Start with the application client ID unless you intentionally use a custom app ID URI.

Sign-in or Graph-backed profile lookup fails:

- `Microsoft Graph > Delegated > User.Read` is missing.
- Admin consent or user consent has not been granted as required by your tenant policy.

Defender ingestion cannot get tokens:

- One or more required `WindowsDefenderATP` application permissions are missing:
  - `Machine.Read.All`
  - `Score.Read.All`
  - `Software.Read.All`
  - `Vulnerability.Read.All`
- Admin consent has not been granted.
- The configured source credentials do not match the app registration you intended to use.
