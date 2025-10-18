# macOS Quick Start

1. Install prerequisites (Homebrew, Docker Desktop, .NET 8 SDK).
2. Clone the repository and run `docker compose up --build`.
3. Seed demo data: `bash scripts/seed-demo.sh`.
4. Verify the API: `curl http://localhost:8080/health`.
5. Use the admin CLI: `dotnet run --project tools/FfaasLite.AdminCli -- --api-key dev-editor-token flags list`.

