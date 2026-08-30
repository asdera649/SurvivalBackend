# SurvivalBackend

ASP.NET Core backend for the mobile survival game server registry, connection lookup and scheduled wipes.

## Configuration

Production secrets must be provided through environment variables or a secret store. Do not commit real keys.

Required production variables:

```powershell
$env:Edgegap__Token="token <edgegap-token>"
$env:S3__BucketName="<bucket-name>"
$env:S3__AccessKey="<s3-access-key>"
$env:S3__SecretKey="<s3-secret-key>"
$env:Security__ServerApiKey="<long-random-server-key>"
$env:Security__AdminApiKey="<long-random-admin-key>"
```

Optional variables:

```powershell
$env:GameClient__CurrentVersion="0.0.1"
$env:Wipe__DayOfWeek="Monday"
$env:Wipe__Time="10:50"
$env:Wipe__TimeZone="Europe/Moscow"
$env:S3__CredentialDeliveryMode="PresignedUrls"
$env:ServerRegistry__StorageMode="S3"
```

`S3__CredentialDeliveryMode=PresignedUrls` is the safe default. It returns short-lived GET/PUT URLs for a single server save. Use `RawCredentials` only as a temporary compatibility mode for old game server builds.

## Protected Requests

Game server callbacks require:

```http
X-Server-Api-Key: <server-api-key>
```

Admin requests require:

```http
X-Admin-Api-Key: <admin-api-key>
```

Development disables API keys and uses a local registry file through `appsettings.Development.json`.

## Endpoints

Public:

- `GET /`
- `GET /health`
- `GET /ready`
- `GET /ActualGameClientData/currentVersion`
- `GET /ServersWipe/remainingTimeToWipe`
- `GET /ServersManagement/servers?clientVersion=...`
- `GET /ServersManagement/connect?uniqueId=...&clientVersion=...`

Game server:

- `GET /ServersManagement/registerServer?requestId=...`
- `POST /ServersManagement/setServerReady?requestId=...`
- `POST /ServersManagement/updateServerState?requestId=...`

Admin:

- `GET /admin`
- `GET /admin/api/overview`
- `GET /admin/api/config`
- `POST /admin/api/wipe/run`
- `POST /admin/api/servers/release-missing`

## Production Notes

- Rotate the old Edgegap and S3 keys if they were ever committed.
- Keep S3 for world save files unless there is a strong reason to proxy saves through the backend.
- Move server registry metadata to PostgreSQL before horizontal scaling to multiple backend instances.
- Keep `bin/`, `obj/`, `.vs/`, local logs and runtime data out of git.
