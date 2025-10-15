# Roadmap

## Current Snapshot
- **API**: Minimal flag CRUD, evaluation, audit log, health check, SSE stream, and WebSocket placeholder implemented in ASP.NET Core 8.
- **Storage**: PostgreSQL `jsonb` for flags/audit with Entity Framework Core migrations; Redis-backed cache with basic invalidation.
- **SDK**: .NET client with local cache, SSE sync, evaluate helper extensions, sample console app, and unit tests.
- **Tooling**: Dockerfile + docker-compose for local stack, GitHub Actions CI (build/test, Docker lint) and release pipelines (Docker image, NuGet).
- **Coverage**: Unit tests around evaluator and SDK happy-paths; lacks integration coverage.

## Next Release (v0.2.0) Goals
### Hardening the Service
- Add API authentication (PAT or JWT) and role separation for write operations; surface authenticated actor in `AuditEntry`.
- Implement optimistic concurrency (row version or `UpdatedAt` check) and improve cache invalidation to avoid stale reads after concurrent updates.
- Finalize the WebSocket channel or remove the stub; align SSE payloads with typed change events (`created/updated/deleted`) and add heartbeat/auto-reconnect guidance.
- Provide migration automation in Docker entrypoint and document rolling upgrade steps.
- Externalize API keys/secrets to managed secret stores with rotation guidance and remove plaintext configuration.

### Developer & User Experience
- Ship an admin UI (or minimal CLI) for managing flags and viewing audits instead of manual API calls.
- Extend targeting operators (greater-than, regex, segments) and add percentage rollouts; document targeting evaluation order with examples.
- Deliver richer SDK ergonomics: preload flags on startup, typed helpers per flag, resilience policies, optional background refresh without SSE.
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
- Initial MVP shipped: CRUD API, evaluation engine, Redis cache, SSE broadcast, .NET SDK, CI, and release automation.
- Sample console app added to demonstrate SDK usage.
- First EF Core migration created and validated against PostgreSQL 16.
- API authentication added with role-based API keys and audit actor attribution.

## Contributing
1. Discuss ideas via GitHub issues before large changes; include use-case and acceptance criteria.
2. Fork, branch from `develop`, and add unit/integration tests for new behavior.
3. Ensure `dotnet test` and lint workflows pass; supply documentation updates alongside code.
4. Follow semantic commits and keep PRs focused; maintainers will squash when merging.
