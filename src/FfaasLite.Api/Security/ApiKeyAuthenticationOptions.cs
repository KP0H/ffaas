using System.Collections.Generic;

using Microsoft.AspNetCore.Authentication;

namespace FfaasLite.Api.Security;

public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string DefaultScheme = AuthConstants.Schemes.ApiKey;

    public string HeaderName { get; set; } = "Authorization";
    public string Prefix { get; set; } = "Bearer ";
    public string AlternativeHeader { get; set; } = "X-Api-Key";
    public List<ApiKeyCredential> ApiKeys { get; set; } = new();
}
