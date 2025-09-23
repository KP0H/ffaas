# NOTES

## Entity Framework

Add migration
```powershell
dotnet ef migrations add Init -p src/FfaasLite.Infrastructure -s src/FfaasLite.Api -o Db/Migrations
```

Remove migration
```
dotnet ef migrations remove
```

Apply migration
```powershell
dotnet ef database update -s src/FfaasLite.Api
```