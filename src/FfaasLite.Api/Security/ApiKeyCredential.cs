using System;

namespace FfaasLite.Api.Security;

public class ApiKeyCredential
{
    public string Name { get; set; } = string.Empty;
    public string? Key { get; set; } = string.Empty;
    public string? Hash { get; set; }
    public string[] Roles { get; set; } = Array.Empty<string>();
}
