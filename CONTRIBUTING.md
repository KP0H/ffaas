# Contributing to FFaaS Lite

Thank you for your interest in improving FFaaS Lite. This project delivers a .NET-based feature flag service (API, SDK, admin CLI, and infrastructure tooling). The guidelines below summarise the expectations for issues, merge requests, testing, and documentation so we can collaborate effectively.

## Guiding Principles
- Discuss significant changes early. Open an issue or start a discussion referencing the relevant roadmap item in `docs/roadmap.md`.
- Keep contributions focused. Prefer smaller merge requests with a clear scope over broad, multi-feature changes.
- Automate validation. Every change should pass the test suite and, when relevant, linting/formatting checks before review.
- Document user-visible behaviour. Update README, guides under `docs/`, and samples when behaviour changes.

## Getting Started
1. **Fork and clone** the repository.
2. **Create a feature branch** from `master` (e.g., `feature/short-description`).
3. **Install prerequisites** listed below.
4. **Restore dependencies and build** the solution.

### Prerequisites
- .NET 8 SDK (`dotnet --info` should show version `8.x`).
- Docker Desktop (optional but recommended for running the stack via `docker compose`).
- PostgreSQL 16+ and Redis 7+ if you prefer running services locally without Docker.
- PowerShell 7+ (Windows/macOS/Linux) for helper scripts in `tools/` and `gen-canvas.ps1`.

### Environment Setup
```powershell
git clone <your-fork-url>
cd ffaas
dotnet restore FfaasLite.sln
dotnet build FfaasLite.sln
```

To spin up the full stack (API + PostgreSQL + Redis) locally:
```powershell
docker compose up --build
```

For a local-only run without Docker:
```powershell
dotnet ef database update -s src/FfaasLite.Api -p src/FfaasLite.Infrastructure
dotnet run --project src/FfaasLite.Api
```

You can verify the SDK sample after the API is running:
```powershell
dotnet run --project samples/FfaasLite.ConsoleSample
```

## Development Workflow
- **Branch naming:** `feature/<topic>`, `bugfix/<topic>`, or `chore/<topic>`.
- **Issue tracking:** Use the templates in `.gitlab/issue_templates/`. Always link the issue in your merge request description.
- **Commit style:** Follow [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/) (`feat:`, `fix:`, `chore:` etc.) to make changelog generation easier.
- **Merge requests:** Use the default template under `.gitlab/merge_request_templates/`. Fill in each section and include links to the related issue(s).

## Coding Standards
- This repository targets C# 12 / .NET 8. Prefer modern language features when they simplify the code without sacrificing clarity.
- Enable nullable reference types for new projects/files and avoid suppressions unless justified.
- File-scoped namespaces are preferred in new files.
- Keep business rules close to the domain (`src/FfaasLite.Core`) and infrastructure concerns inside `src/FfaasLite.Infrastructure`.
- When contributing to the SDK (`src/FfaasLite.SDK`), consider API compatibility. Breaking changes must be called out in the merge request template checklist.
- For configuration or migrations, prefer strongly typed options and EF Core migrations under `src/FfaasLite.Infrastructure/Migrations`.

### Formatting & Static Analysis
- Run `dotnet format FfaasLite.sln --verify-no-changes` before committing; the CI will enforce formatting.
- Address CA/Roslyn analyzer warnings introduced by your change. Suppressions require justification in-code.
- Shell scripts and PowerShell scripts should follow the existing style and include inline help when adding new commands.

## Testing Expectations
- Ensure `dotnet test` passes for the full solution:
  ```powershell
  dotnet test FfaasLite.sln --configuration Release
  ```
- Add unit tests under `tests/FfaasLite.Tests` for API/core changes and under `tests/FfaasLite.AdminCli.Tests` when touching the CLI.
- For features impacting the realtime flow or persistence, consider integration tests that exercise PostgreSQL/Redis (Docker services can be reused via `docker compose`).
- Update or add test fixtures so that new behaviour is covered; avoid editing tests just to bypass new failures.

## Documentation & Samples
- Update `README.md`, `docs/`, or `canvas.md` when behaviour, configuration, or architecture changes.
- SDK-facing changes should include updates to the sample (`samples/FfaasLite.ConsoleSample`) or additional snippets when helpful.
- Add diagrams or checklists to `docs/guides/` if the change introduces new workflows or operational steps.
- When adding new configuration, document environment variables and appsettings keys.

## GitLab Templates
- **Issue templates:** `.gitlab/issue_templates/` contains standard templates for bugs, features, chores, and security reports. Use them when opening issues so maintainers get consistent context.
- **Merge request template:** `.gitlab/merge_request_templates/default.md` defines the required checklist (tests, docs, breaking changes) and links to related issues. It is automatically selected for new merge requests.

## Release Considerations
- For SDK changes, update package metadata if the public surface area changes (`src/FfaasLite.SDK/FfaasLite.SDK.csproj`).
- Document migration steps when schema changes are introduced (`docs/guides/operations.md` or a new guide if appropriate).
- Tag releases following `v<major>.<minor>.<patch>` and update the changelog (future automation can rely on Conventional Commits).

## Security & Responsible Disclosure
- Do **not** file public issues for suspected vulnerabilities.
- Create a **confidential** issue using the `Security Report` template or contact the maintainers listed in `CODEOWNERS`.
- Provide reproduction steps, impact assessment, and affected versions. Maintainers will coordinate fixes and disclosure timelines.

## Community Conduct
- Be respectful and constructive in code reviews and discussions.
- Provide actionable feedback backed by technical reasoning.
- Remember that reviewers volunteer their time; respond to feedback promptly or comment if more time is needed.

By following these guidelines you help ensure FFaaS Lite remains reliable, maintainable, and welcoming to new contributors. We appreciate your support!
