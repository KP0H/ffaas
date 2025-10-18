# Roadmap

## Current Snapshot
- **API**: Flag CRUD, evaluation, audit log, health check, a hardened SSE stream (heartbeats/retry hints), and automatic EF Core migrations on ASP.NET Core 8.
- **Storage**: PostgreSQL `jsonb` for flags/audit with Entity Framework Core migrations; Redis-backed cache with basic invalidation.
- **SDK**: .NET client with local cache, realtime SSE sync (heartbeats/backoff), snapshot helpers, sample console app, and unit tests.
- **Tooling**: Dockerfile + docker-compose for local stack, GitHub Actions CI (build/test, Docker lint) and release pipelines (Docker image, NuGet).
- **Coverage**: Unit tests plus realtime SSE/SDK integration tests; broader end-to-end coverage with Postgres/Redis remains outstanding.

## Next Release (v0.2.0) Goals
### Hardening the Service
- Add API authentication (PAT or JWT) and role separation for write operations; surface authenticated actor in `AuditEntry`.
- Implement optimistic concurrency (row version or `UpdatedAt` check) and improve cache invalidation to avoid stale reads after concurrent updates.
- Persist a short-lived SSE journal for clients using `Last-Event-ID` and publish connection/heartbeat metrics.
- Add automated schema drift detection and alerting when migrations are pending.
- Externalize API keys/secrets to managed secret stores with rotation guidance and remove plaintext configuration.
- Publish ETag headers for flag resources so clients can rely on `If-Match` instead of custom timestamps.

### Developer & User Experience
- Deliver richer SDK ergonomics: preload flags on startup, typed helpers per flag, resilience policies, optional background refresh without SSE.
- Enhance the admin CLI with interactive flows and bulk import/export capabilities.
- Provide rule authoring UX (templates/validation) to simplify advanced targeting configuration.
- Write quick-start guides for Windows/macOS/Linux, including sample seed scripts for flags.

### Observability & Quality
- Add structured logging, OpenTelemetry traces, and Prometheus metrics for flag evaluations and cache hits.
- Create integration tests that spin up PostgreSQL/Redis (can reuse docker-compose) and smoke-test critical endpoints.
- Configure automated dependency scanning and license compliance (e.g., GitHub Dependabot, OSS Review Toolkit report).
- Publish API reference (Swagger export) and SDK API docs (DocFX or XML docs surfaced on learn.microsoft.com).
- Capture authentication failure metrics/log enrichment and expose alerts for suspicious activity.

## Backlog & Stretch Ideas
- Multi-environment support (dev/stage/prod) with flag promotion flows.
- Scheduled flag changes and time-based rollouts.
- Segment management (user lists, cohorts) with bulk upload.
- Multi-language SDKs (JavaScript/TypeScript, Go) and REST API parity tests.
- UI/SDK support for experiments (A/B testing) with metrics hooks.

## Recently Completed
- Realtime channel stabilised: structured SSE events, heartbeat/resend hints, WebSocket retired, SDK backoff/heartbeat support, and integration coverage.
- Initial MVP shipped: CRUD API, evaluation engine, Redis cache, SSE broadcast, .NET SDK, CI, and release automation.
- Targeting enhancements: numeric comparisons, regex, segment matching, and percentage rollouts with updated documentation/tests.
- Admin CLI delivered: flag CRUD, optimistic concurrency aware upsert, audit viewer, and CLI docs/tests.
- Migration automation & upgrade playbook: startup hosted service with retries, skip flag, and documented rollout guidance.
- Sample console app added to demonstrate SDK usage.
- First EF Core migration created and validated against PostgreSQL 16.
- API authentication added with role-based API keys and audit actor attribution.

## Contributing
1. Discuss ideas via GitHub issues before large changes; include use-case and acceptance criteria.
2. Fork, branch from `develop`, and add unit/integration tests for new behavior.
3. Ensure `dotnet test` and lint workflows pass; supply documentation updates alongside code.
4. Follow semantic commits and keep PRs focused; maintainers will squash when merging.
