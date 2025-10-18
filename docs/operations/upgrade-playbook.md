# Upgrade & Rollback Playbook

## 1. Preparation Checklist
- [ ] Confirm the target release tag and migration scripts (see `src/FfaasLite.Infrastructure`).
- [ ] Verify database backups are recent and restorable.
- [ ] Ensure the `FFAAAS_SKIP_MIGRATIONS` flag is **unset** in the environment that should auto-apply migrations.
- [ ] Communicate the maintenance window (if any) to downstream teams.
- [ ] Capture baseline metrics (API latency, DB load, error rate) for comparison.

## 2. Rolling Upgrade (single instance)
1. Deploy the new container image.
2. On startup the API runs EF Core migrations automatically. Watch logs for `Database migration completed`.
3. Run smoke tests (`/health`, `/api/flags`, `/api/evaluate`).
4. Monitor metrics for 5–10 minutes and confirm no error spikes.

## 3. Multi-instance / Blue-Green
1. Set `FFAAAS_SKIP_MIGRATIONS=true` on all **existing** instances to prevent race conditions.
2. Provision a standby (green) slice with the new image and leave migrations enabled there.
3. Wait for migration success, then swap traffic (load balancer or ingress switch).
4. After confidence, tear down (or repurpose) the old slice and re-enable automation on remaining nodes.

## 4. Canary Rollout
1. Pick one instance in the pool and allow migrations (flag disabled on the rest).
2. Route a small percentage of traffic (5–10 %) to the canary.
3. Observe error rate, DB locks, and migration duration.
4. Gradually expand to the full fleet, removing the skip flag along the way.

## 5. Rollback Strategy
- If migrations added non-destructive changes (columns with defaults, new tables), redeploy the previous image with `FFAAAS_SKIP_MIGRATIONS=true` to ignore forward-only migrations.
- For destructive changes, restore from the latest backup and redeploy the previous release (ensure connections are drained first).
- Always keep a copy of the migration SQL (generated via `dotnet ef migrations script`) for manual reversals.

## 6. Local Development & CI
- Docker Compose setups rely on the startup service; no manual `dotnet ef` invocations are needed.
- To skip locally (e.g., when pointing to shared dev DB) set `FFAAAS_SKIP_MIGRATIONS=true` or add to `appsettings.Development.json`:
  ```json
  {
    "Database": {
      "Migrations": {
        "Skip": true
      }
    }
  }
  ```
- CI pipelines can run `dotnet test` followed by `dotnet publish`; migrations will execute during integration tests when using relational providers.

## 7. Observability
- Log search: `DatabaseMigrationHostedService` for attempts and failures.
- Suggested dashboards:
  - Migration duration (`database_migration_duration_seconds` once metrics are wired).
  - Error counts by service version.
  - PostgreSQL locks/blocking sessions during deployment.

## 8. Troubleshooting
| Symptom | Action |
| ------- | ------ |
| Timeout connecting to PostgreSQL | Increase `Database:Migrations:MaxRetryCount` / `RetryDelaySeconds` or fix networking/credentials. |
| `The database provider does not support Migrate` | Happens in integration tests with the in-memory provider; automation falls back to `EnsureCreated`. |
| Duplicate migration attempts across nodes | Ensure only one node has automation enabled during rolling upgrades, or rely on the migration lock PostgreSQL provides. |
| Schema drift detected post-upgrade | Compare migration history (`__EFMigrationsHistory`) between environments and re-run the image with automation enabled. |
