using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FfaasLite.Api.Security;

public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var providedKey = ExtractApiKey();
        if (string.IsNullOrWhiteSpace(providedKey))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        Logger.LogDebug("Configured API keys: {Keys}", string.Join(",", Options.ApiKeys.Select(k => $"{k.Name}:{k.Key}:{k.Hash}")));
        Logger.LogDebug("Provided API key attempted: {Key}", providedKey);

        var match = Options.ApiKeys.FirstOrDefault(k => KeyMatches(k, providedKey));
        if (match is null)
        {
            Logger.LogWarning("API key authentication failed for request from {RemoteIp}", Context.Connection.RemoteIpAddress);
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, match.Name),
            new Claim(ClaimTypes.Name, match.Name)
        };

        if (match.Roles is not null)
        {
            foreach (var role in match.Roles)
            {
                if (!string.IsNullOrWhiteSpace(role))
                {
                    claims.Add(new Claim(ClaimTypes.Role, role));
                }
            }
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        if (!Response.Headers.ContainsKey("WWW-Authenticate"))
        {
            var prefix = Options.Prefix?.Trim() ?? "Bearer";
            Response.Headers.Append("WWW-Authenticate", $"{prefix} realm=\"ffaas\"");
        }
        return Task.CompletedTask;
    }

    private string? ExtractApiKey()
    {
        if (!string.IsNullOrEmpty(Options.HeaderName) &&
            Request.Headers.TryGetValue(Options.HeaderName, out var headerValues))
        {
            var headerValue = headerValues.ToString();
            if (!string.IsNullOrEmpty(Options.Prefix))
            {
                if (headerValue.StartsWith(Options.Prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return headerValue.Substring(Options.Prefix.Length).Trim();
                }
            }
            else
            {
                return headerValue.Trim();
            }
        }

        if (!string.IsNullOrEmpty(Options.AlternativeHeader) &&
            Request.Headers.TryGetValue(Options.AlternativeHeader, out var altValues))
        {
            return altValues.ToString().Trim();
        }

        return null;
    }

    private static bool KeyMatches(ApiKeyCredential credential, string providedKey)
    {
        if (!string.IsNullOrEmpty(credential.Key) &&
            string.Equals(credential.Key, providedKey, StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(credential.Hash))
        {
            var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(providedKey));
            try
            {
                if (TryDecodeHash(credential.Hash, out var storedHash))
                {
                    var result = CryptographicOperations.FixedTimeEquals(storedHash, providedHash);
                    Array.Clear(storedHash);
                    return result;
                }
            }
            finally
            {
                Array.Clear(providedHash);
            }
        }

        return false;
    }

    private static bool TryDecodeHash(string value, out byte[] decoded)
    {
        decoded = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(value)) return false;

        if (IsHexString(value))
        {
            try
            {
                decoded = Convert.FromHexString(value);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        try
        {
            decoded = Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsHexString(string value)
    {
        if (value.Length % 2 != 0) return false;
        foreach (var c in value)
        {
            var isHex = c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
            if (!isHex) return false;
        }
        return true;
    }
}
