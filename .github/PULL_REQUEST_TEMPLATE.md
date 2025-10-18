## Summary
- Explain what this pull request changes and why.
- Highlight any follow-up work that is intentionally deferred.

## Related Issues
- Closes #ISSUE-ID
- References #ISSUE-ID

## Testing
- [ ] `dotnet test FfaasLite.sln`
- [ ] `dotnet format FfaasLite.sln --verify-no-changes`
- [ ] Additional commands (e.g., `docker compose up`, manual API checks):
  ```text
  <commands and results>
  ```

## Deployment / Rollout Notes
- Configuration changes (environment variables, appsettings)
- Database migrations or cache invalidation steps
- Operational checklists for on-call or SRE teams

## Breaking Changes
- [ ] This change introduces a breaking API / SDK contract
  - Impacted consumers and required actions:

## Documentation
- [ ] README / docs updated
- [ ] Samples updated

## Checklist
- [ ] Linked to a tracked issue
- [ ] Added or updated tests
- [ ] Added or updated telemetry/logging if behaviour changes
- [ ] Performed self-review of code and comments
- [ ] Verified that new or changed endpoints are covered by authentication/authorization rules
