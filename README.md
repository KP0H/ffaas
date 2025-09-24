# ffaas
FFaaS-lite — Feature Flags as Code (MVP)

Minimal service for Feature Flags with SDK for .NET:
- **API**: ASP.NET Core 8
- **Хранение**: PostgreSQL (`jsonb`)
- **Кэш**: Redis
- **Real-time**: SSE (Server-Sent Events) + WebSocket draft
- **SDK (.NET)**: local cache, SSE-subscription (optional), `EvaluateAsync`
- **Flags**: boolean / string / number + attributes targeting (`country`, `userId`, etc.)
- **Audit**: `AuditEntry` with diffs (jsonb)
- **SemVer**, CI, Docker

---

## Table of content
- [Quick launch](#quick-launch)
  - [In Docker (Linux/Windows/macOS)](#в-docker-linuxwindowsmacos)
  - [Local: Windows](#local-windows)
  - [Local: Linux/macOS](#local-linuxmacos)
- [DB Migrations](#db-migrations)
- [API](#api)
- [SDK (.NET)](#sdk-net)
- [Sample](#sample)
- [Cache](#cache)
- [Realtime](#realtime)
- [CI/CD](#cicd)
- [License](#license)

---