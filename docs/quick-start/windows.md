# Windows Quick Start

1. Install prerequisites (Docker Desktop, .NET 8 SDK).
2. Clone the repository and run `docker compose up --build`.
3. Seed demo data: `powershell -ExecutionPolicy Bypass -File scripts/seed-demo.ps1`.
4. Verify the API: `curl http://localhost:8080/health`.
5. Use the admin CLI: `dotnet run --project tools/FfaasLite.AdminCli -- --api-key dev-editor-token flags list`.

