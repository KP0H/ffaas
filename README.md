# FFaaS Lite

Feature Flags as Code - minimal infrastructure for shipping boolean, string, and number feature flags with attribute targeting.

[![NuGet](https://img.shields.io/nuget/v/FfaasLite.SDK.svg)](https://www.nuget.org/packages/FfaasLite.SDK)
[![GitHub package](https://img.shields.io/badge/packages-github-blue)](https://github.com/KP0H/ffaas/pkgs/nuget/FfaasLite.SDK)

## Highlights
- ASP.NET Core 8 HTTP API with CRUD for flags, evaluation endpoint, structured SSE stream (heartbeats + retries), basic audit log, automatic EF Core migrations, and health check.
- PostgreSQL (`jsonb`) persistence with Entity Framework Core migrations; Redis cache with simple invalidation strategy.
- .NET SDK with local cache, realtime SSE synchronisation (backoff/heartbeats), helper extensions, and sample console client.
- Dockerfile + docker-compose for local stack, GitHub Actions for CI, Docker image publishing, and NuGet trusted publishing.
- Unit tests for the flag evaluator and SDK client behavior.

> **Status:** MVP suitable for experimentation; security and observability hardening still pending. See `docs/roadmap.md` for upcoming work.

## Architecture Overview
```
[Clients / SDK]
      |
      v
   HTTP API (ASP.NET Core Minimal APIs)
      |
  +-----------+
  | Postgres  |  <-- flags, rules, audit (jsonb)
  | Redis     |  <-- cache for flags / evaluation
  +-----------+
```

The API exposes CRUD for feature flags, an `Evaluate` endpoint used by SDKs, and a structured SSE stream (`/api/stream`) that pushes typed change events with periodic heartbeats. The former WebSocket endpoint now returns **410 Gone** in favour of the hardened SSE channel.

## Quick Start
### Using Docker Compose
1. Ensure Docker Desktop is running.
2. Launch the stack (API + PostgreSQL + Redis):
   ```powershell
   docker compose up --build
   ```
3. The API listens on http://localhost:8080. Swagger UI: http://localhost:8080/swagger.
4. PostgreSQL and Redis are exposed on the default ports (5432, 6379) for inspection.

### Running the API Locally
1. Install prerequisites: .NET 8 SDK, PostgreSQL 16+, Redis 7+.
2. Set the connection strings in `appsettings.Development.json` or via environment variables (`ConnectionStrings__postgres`, `ConnectionStrings__redis`).
3. Apply the initial migration:
   ```powershell
   dotnet ef database update -s src/FfaasLite.Api -p src/FfaasLite.Infrastructure
   ```
4. Run the API:
   ```powershell
   dotnet run --project src/FfaasLite.Api
   ```

### SDK Sample
Run the console sample once the API is reachable:
```powershell
cd samples/FfaasLite.ConsoleSample
dotnet run
```

## Authentication
Write operations now require an API key. Two roles are supported:

- `Editor` - create/update/delete flags.
- `Reader` - access read-only admin surfaces such as `/api/audit`.

For local development, `appsettings.Development.json` seeds two keys:

| Role   | Name           | Token              |
| ------ | -------------- | ------------------ |
| Reader | `local-reader` | `dev-reader-token` |
| Editor | `local-editor` | `dev-editor-token` |

Send the token with either header:

```text
Authorization: Bearer dev-editor-token
```

or

```text
X-Api-Key: dev-editor-token
```

To configure keys for other environments, add entries under `Auth.ApiKeys` or use environment variables, e.g.:

```powershell
$env:Auth__ApiKeys__0__Name = 'ci-editor'
$env:Auth__ApiKeys__0__Key = '<generate-strong-token>'
$env:Auth__ApiKeys__0__Roles__0 = 'Editor'
```

For production, prefer sourcing secrets from environment variables or secret stores. You can also avoid plaintext keys by providing a SHA-256 hash via the optional `Hash` property (hex or base64):

```json
"Auth": {
  "ApiKeys": [
    {
      "Name": "ci-editor",
      "Hash": "<sha256-hex-or-base64>",
      "Roles": [ "Editor" ]
    }
  ]
}
```

Invalid authentication attempts are logged (with remote IP metadata) for auditing.

### Data Protection Keys
The API persists ASP.NET Core Data Protection keys when `DataProtection__KeysDirectory` is set (docker-compose mounts `/var/ffaas/dataprotection`). For multi-instance or production deployments, mount a shared volume or configure a dedicated key ring (Azure Blob, S3, Redis) and optionally protect keys with an encryptor (Azure Key Vault, Windows DPAPI).

> **Note:** During local development you will see `No XML encryptor configured` warnings because keys are stored in plaintext on the mounted volume. This is expected for dev environments; configure an encryptor (e.g., Azure Key Vault) or store keys on encrypted media before going to production.

### Updating Flags Safely
Clients must provide the `lastKnownUpdatedAt` value they received from a previous GET/POST when issuing a PUT. The server compares this timestamp and returns **409 Conflict** if another update wins the race. Example:

```powershell
$flag = Invoke-RestMethod -Method Get -Uri http://localhost:8080/api/flags/new-ui
$body = @{
  type = 'boolean'
  boolValue = $true
  lastKnownUpdatedAt = $flag.updatedAt
} | ConvertTo-Json
Invoke-RestMethod -Method Put -Uri http://localhost:8080/api/flags/new-ui -Body $body -ContentType 'application/json'
```

Handle `409` responses by refreshing the flag and retrying with the latest `updatedAt` stamp.

Without a valid key, `POST`/`PUT`/`DELETE` requests return `401`/`403` and audit entries capture the authenticated actor name. Read-only endpoints remain publicly accessible by default.

## API Surface
| Method | Path | Description |
| ------ | ---- | ----------- |
| GET    | `/health` | Health probe. |
| GET    | `/api/flags` | List all flags (cached). |
| GET    | `/api/flags/{key}` | Retrieve a single flag. |
| POST   | `/api/flags` | Create a flag (requires `Editor`). |
| PUT    | `/api/flags/{key}` | Update flag definition (requires `Editor`, include `lastKnownUpdatedAt`). |
| DELETE | `/api/flags/{key}` | Delete flag (requires `Editor`). |
| POST   | `/api/evaluate/{key}` | Evaluate against a context payload. |
| GET    | `/api/audit` | Return recent audit entries (requires `Reader`). |
| GET    | `/api/stream` | Structured SSE feed with typed change events + heartbeats. |
| GET    | `/ws` | Returns 410 Gone (WebSocket channel removed in favour of SSE). |

### Example
```powershell
$context = @{ userId = "user-1"; attributes = @{ country = "NL" } } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri http://localhost:8080/api/evaluate/new-ui -Body $context -ContentType 'application/json'
```

### Realtime Events
- Each message arrives as `event: flag-change` with an incrementing `id` and JSON payload `{ "type": "created|updated|deleted", "version": long, "payload": { "key": "...", "flag": { ... } } }`.
- Heartbeats (`event: heartbeat`) ship every 15 seconds; the stream also emits `retry: 5000` hints that clients can honour when reconnecting.
- WebSocket consumers should migrate to SSE; the server now returns 410 for `/ws`.

## Database Migrations
- The API runs `DbContext.Database.MigrateAsync()` on startup (or `EnsureCreatedAsync` for in-memory tests) via `DatabaseMigrationHostedService`.
- Control behaviour with configuration: set `Database:Migrations:Skip=true` or the environment variable `FFAAAS_SKIP_MIGRATIONS=true` to disable automation.
- Adjust retry behaviour through `Database:Migrations:MaxRetryCount` (default 5) and `Database:Migrations:RetryDelaySeconds` (default 5).
- See `docs/operations/upgrade-playbook.md` for rolling upgrade and rollback guidance.

## Data Model
- `Flag`: `Key`, `Type` (`boolean|string|number`), default value, optional rules, `UpdatedAt`.
- `TargetRule`: attribute name, operator (`eq`, `ne`, `contains`), comparison value, optional priority, override values.
- `AuditEntry`: action (`create|update|delete`), `FlagKey`, `Actor` (defaults to `system`), diff snapshot.

Rules are executed by ascending `Priority`. When priority is `null`, the rule is evaluated last. If no rule matches, the default flag value is used.

## .NET SDK
Install from NuGet:
```powershell
Install-Package FfaasLite.SDK
```

Usage:
```csharp
var client = new FlagClient("http://localhost:8080");
await client.StartRealtimeAsync(); // optional FlagStreamOptions lets you tweak backoff/heartbeat

var context = new EvalContext(
    UserId: "user-42",
    Attributes: new() { ["country"] = "NL" }
);

var result = await client.EvaluateAsync("new-ui", context);
if (result.AsBool() == true)
{
    // enable new experience
}
```

SDK features:
- HTTP evaluation with automatic JSON normalization.
- Realtime SSE subscription with structured change events, heartbeats, and configurable reconnect backoff.
- Helper extensions `AsBool`, `AsString`, and `AsNumber`.
- Designed for dependency injection by supplying a configured `HttpClient`.

See `src/FfaasLite.SDK/README.md` for additional details.

## Development
- Restore/build/test:
  ```powershell
  dotnet restore FfaasLite.sln
  dotnet build FfaasLite.sln -c Release
  dotnet test tests/FfaasLite.Tests/FfaasLite.Tests.csproj
  ```
- Coding standards: nullable enabled, treat warnings as errors in Release.
- Integration tests are not yet present - consider contributing coverage for API + database.
- Dockerfile publishes a self-contained runtime image (ASP.NET 8 base).

## CI/CD
- `ci.yml`: multi-platform build and test, test artifacts uploaded, Dockerfile lint via Hadolint.
- `release-docker.yml`: builds and pushes multi-arch images to GHCR on `v*.*.*` tags.
- `release-nuget.yml`: trusted publishing flow for the SDK using OIDC login.

## Roadmap & Support
- Planned improvements are tracked in `docs/roadmap.md`.
- Questions, bugs, or feature requests: open an issue on GitHub.

## License
Licensed under the MIT License. See `LICENSE` for details.











