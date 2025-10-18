using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using FfaasLite.Core.Models;

namespace FfaasLite.AdminCli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        return await AdminApp.RunAsync(args, Console.Out, Console.Error);
    }
}

internal static class HttpClientBuilder
{
    public static HttpMessageHandler CreateHandler()
        => new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
            UseProxy = false,
            Proxy = null,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(10)
        };
}

internal static class AdminApp
{
    public static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr, HttpClient? clientOverride = null)
    {
        try
        {
            var config = AdminConfig.Parse(args);
            var jsonOptions = JsonOptions.Create();
            var httpClient = clientOverride ?? CreateHttpClient(config.BaseUri, config.ApiKey);
            if (clientOverride is null)
            {
                ConfigureClient(httpClient, config.ApiKey);
            }

            var context = new CliContext(httpClient, stdout, stderr, jsonOptions);
            return await CommandDispatcher.DispatchAsync(context, config.CommandArgs);
        }
        catch (CliException cliEx)
        {
            await stderr.WriteLineAsync(cliEx.Message);
            if (!string.IsNullOrWhiteSpace(cliEx.Detail))
            {
                await stderr.WriteLineAsync(cliEx.Detail);
            }
            return 1;
        }
        catch (Exception ex)
        {
            await stderr.WriteLineAsync($"Unexpected error: {ex.Message}");
            return 1;
        }
    }

    private static HttpClient CreateHttpClient(Uri baseUri, string apiKey)
    {
        var handler = HttpClientBuilder.CreateHandler();
        var client = new HttpClient(handler)
        {
            BaseAddress = baseUri,
            Timeout = TimeSpan.FromSeconds(30)
        };
        ConfigureClient(client, apiKey);
        return client;
    }

    private static void ConfigureClient(HttpClient client, string apiKey)
    {
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        if (!client.DefaultRequestHeaders.Contains("X-Api-Key"))
        {
            client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        }
    }
}

internal sealed record AdminConfig(Uri BaseUri, string ApiKey, string[] CommandArgs)
{
    private const string DefaultUrl = "http://localhost:8080";
    private const string UrlOption = "--url";
    private const string ApiKeyOption = "--api-key";
    private const string UrlEnv = "FFAAAS_API_URL";
    private const string TokenEnv = "FFAAAS_API_TOKEN";

    public static AdminConfig Parse(string[] args)
    {
        string? url = null;
        string? apiKey = null;
        var remaining = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var current = args[i];
            switch (current)
            {
                case UrlOption:
                    if (i + 1 >= args.Length)
                    {
                        throw new CliException("Missing value for --url option.");
                    }
                    url = args[++i];
                    break;
                case ApiKeyOption:
                    if (i + 1 >= args.Length)
                    {
                        throw new CliException("Missing value for --api-key option.");
                    }
                    apiKey = args[++i];
                    break;
                default:
                    remaining.Add(current);
                    break;
            }
        }

        url ??= Environment.GetEnvironmentVariable(UrlEnv);
        url = string.IsNullOrWhiteSpace(url) ? DefaultUrl : url!.Trim();

        apiKey ??= Environment.GetEnvironmentVariable(TokenEnv);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new CliException("API key is required. Provide --api-key <token> or set FFAAAS_API_TOKEN.");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new CliException($"Invalid API URL '{url}'. Provide a valid absolute URI.");
        }

        return new AdminConfig(uri, apiKey!, remaining.ToArray());
    }
}

internal sealed class CliContext
{
    public CliContext(HttpClient httpClient, TextWriter @out, TextWriter error, JsonSerializerOptions jsonOptions)
    {
        HttpClient = httpClient;
        Out = @out;
        Error = error;
        Json = jsonOptions;
    }

    public HttpClient HttpClient { get; }
    public TextWriter Out { get; }
    public TextWriter Error { get; }
    public JsonSerializerOptions Json { get; }
}

internal static class CommandDispatcher
{
    public static async Task<int> DispatchAsync(CliContext context, string[] commandArgs)
    {
        if (commandArgs.Length == 0)
        {
            await UsagePrinter.PrintAsync(context.Out);
            return 1;
        }

        var primary = commandArgs[0].ToLowerInvariant();
        var subArgs = commandArgs.Skip(1).ToArray();

        return primary switch
        {
            "flags" => await FlagCommands.ExecuteAsync(context, subArgs),
            "audits" => await AuditCommands.ExecuteAsync(context, subArgs),
            "help" => await UsagePrinter.PrintAsync(context.Out),
            "-h" or "--help" => await UsagePrinter.PrintAsync(context.Out),
            _ => await UsagePrinter.UnknownPrimaryAsync(context.Out, primary)
        };
    }
}

internal static class UsagePrinter
{
    public static Task<int> PrintAsync(TextWriter writer)
    {
        var builder = new StringBuilder();
        builder.AppendLine("FFaaS Admin CLI");
        builder.AppendLine();
        builder.AppendLine("Usage:");
        builder.AppendLine("  ffaas-admin [--url <baseUrl>] [--api-key <token>] <command> [options]");
        builder.AppendLine();
        builder.AppendLine("Commands:");
        builder.AppendLine("  flags list");
        builder.AppendLine("  flags get <key>");
        builder.AppendLine("  flags upsert <key> [--type <boolean|string|number>] [--bool-value <true|false>]");
        builder.AppendLine("             [--string-value <text>] [--number-value <value>] [--rules <pathToJson>]");
        builder.AppendLine("  flags delete <key>");
        builder.AppendLine("  audits list [--take <n>]");
        builder.AppendLine();
        builder.AppendLine("Environment Variables:");
        builder.AppendLine("  FFAAAS_API_URL   Base URL (default http://localhost:8080)");
        builder.AppendLine("  FFAAAS_API_TOKEN API key used for Authorization and X-Api-Key headers");

        return writer.WriteAsync(builder.ToString()).ContinueWith(static _ => 0);
    }

    public static async Task<int> UnknownPrimaryAsync(TextWriter writer, string command)
    {
        await writer.WriteLineAsync($"Unknown command '{command}'. See 'ffaas-admin help' for usage.");
        return 1;
    }
}

internal static class FlagCommands
{
    public static async Task<int> ExecuteAsync(CliContext context, string[] args)
    {
        if (args.Length == 0)
        {
            return await FlagsListAsync(context, Array.Empty<string>());
        }

        var action = args[0].ToLowerInvariant();
        var subArgs = args.Skip(1).ToArray();

        return action switch
        {
            "list" => await FlagsListAsync(context, subArgs),
            "get" => await FlagGetAsync(context, subArgs),
            "upsert" => await FlagUpsertAsync(context, subArgs),
            "delete" => await FlagDeleteAsync(context, subArgs),
            _ => await UsagePrinter.UnknownPrimaryAsync(context.Out, $"flags {action}")
        };
    }

    private static async Task<int> FlagsListAsync(CliContext context, string[] _)
    {
        var response = await context.HttpClient.GetAsync("/api/flags");
        if (!response.IsSuccessStatusCode)
        {
            await context.Error.WriteLineAsync($"Failed to list flags: {(int)response.StatusCode} {response.ReasonPhrase}");
            return 1;
        }

        var payload = await response.Content.ReadAsStringAsync();
        var flags = JsonSerializer.Deserialize<List<FlagRecord>>(payload, context.Json) ?? new List<FlagRecord>();

        if (flags.Count == 0)
        {
            await context.Out.WriteLineAsync("No flags found.");
            return 0;
        }

        var table = TableBuilder.Build(
            new[] { "Key", "Type", "Value", "Updated" },
            flags.Select(f => new[]
            {
                f.Key,
                f.Type.ToString().ToLowerInvariant(),
                FormatValue(f),
                f.UpdatedAt.ToString("u")
            }));

        await context.Out.WriteLineAsync(table);
        return 0;
    }

    private static string FormatValue(FlagRecord flag)
        => flag.Type switch
        {
            FlagType.Boolean => flag.BoolValue?.ToString() ?? "null",
            FlagType.String => flag.StringValue ?? "null",
            FlagType.Number => flag.NumberValue?.ToString() ?? "null",
            _ => "n/a"
        };

    private static async Task<int> FlagGetAsync(CliContext context, string[] args)
    {
        if (args.Length == 0)
        {
            await context.Error.WriteLineAsync("Usage: ffaas-admin flags get <key>");
            return 1;
        }

        var key = args[0];
        var response = await context.HttpClient.GetAsync($"/api/flags/{Uri.EscapeDataString(key)}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            await context.Error.WriteLineAsync($"Flag '{key}' not found.");
            return 1;
        }
        if (!response.IsSuccessStatusCode)
        {
            await context.Error.WriteLineAsync($"Failed to fetch flag: {(int)response.StatusCode} {response.ReasonPhrase}");
            return 1;
        }

        var payload = await response.Content.ReadAsStringAsync();
        var flag = JsonSerializer.Deserialize<FlagRecord>(payload, context.Json);
        if (flag is null)
        {
            await context.Error.WriteLineAsync("Unable to parse flag response.");
            return 1;
        }

        await context.Out.WriteLineAsync(JsonSerializer.Serialize(flag, context.Json));
        return 0;
    }

    private static async Task<int> FlagUpsertAsync(CliContext context, string[] args)
    {
        if (args.Length == 0)
        {
            await context.Error.WriteLineAsync("Usage: ffaas-admin flags upsert <key> [options]");
            return 1;
        }

        var key = args[0];
        var options = OptionDictionary.Parse(args.Skip(1));

        FlagRecord? existing = null;
        var getResponse = await context.HttpClient.GetAsync($"/api/flags/{Uri.EscapeDataString(key)}");
        if (getResponse.StatusCode == System.Net.HttpStatusCode.OK)
        {
            var payload = await getResponse.Content.ReadAsStringAsync();
            existing = JsonSerializer.Deserialize<FlagRecord>(payload, context.Json);
        }
        else if (getResponse.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            await context.Error.WriteLineAsync($"Failed to retrieve flag '{key}': {(int)getResponse.StatusCode} {getResponse.ReasonPhrase}");
            return 1;
        }

        var targetType = ParseFlagType(options.TryGet("type"), existing?.Type);
        if (targetType is null)
        {
            await context.Error.WriteLineAsync("Specify --type when creating a new flag (boolean|string|number).");
            return 1;
        }

        var rules = await ReadRulesAsync(options.TryGet("rules"));

        var values = ExtractValues(targetType.Value, options, existing);

        if (existing is null)
        {
            var createDto = new FlagCreateDto(
                key,
                targetType.Value,
                values.BoolValue,
                values.StringValue,
                values.NumberValue,
                rules);

            var response = await context.HttpClient.PostAsync("/api/flags",
                JsonContent.Create(createDto, options: context.Json));
            return await HandleMutationResponse(context, response, $"Created flag '{key}'.", "create");
        }
        else
        {
            var updateDto = new FlagUpdateDto(
                targetType.Value,
                values.BoolValue,
                values.StringValue,
                values.NumberValue,
                rules,
                existing.UpdatedAt);

            var response = await context.HttpClient.PutAsync($"/api/flags/{Uri.EscapeDataString(key)}",
                JsonContent.Create(updateDto, options: context.Json));
            return await HandleMutationResponse(context, response, $"Updated flag '{key}'.", "update");
        }
    }

    private static async Task<List<TargetRule>?> ReadRulesAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new CliException($"Rules file '{fullPath}' not found.");
        }

        var json = await File.ReadAllTextAsync(fullPath);
        return JsonSerializer.Deserialize<List<TargetRule>>(json, JsonOptions.Create());
    }

    private static FlagType? ParseFlagType(string? source, FlagType? fallback)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return fallback;
        }

        return source.ToLowerInvariant() switch
        {
            "bool" or "boolean" => FlagType.Boolean,
            "string" => FlagType.String,
            "number" or "double" => FlagType.Number,
            _ => throw new CliException($"Unsupported flag type '{source}'.")
        };
    }

    private static (bool? BoolValue, string? StringValue, double? NumberValue) ExtractValues(
        FlagType targetType,
        OptionDictionary options,
        FlagRecord? existing)
    {
        return targetType switch
        {
            FlagType.Boolean => (
                BoolValue: ParseNullableBool(options.TryGet("bool-value"), existing?.BoolValue),
                StringValue: null,
                NumberValue: null),
            FlagType.String => (
                BoolValue: null,
                StringValue: options.TryGet("string-value") ?? existing?.StringValue,
                NumberValue: null),
            FlagType.Number => (
                BoolValue: null,
                StringValue: null,
                NumberValue: ParseNullableDouble(options.TryGet("number-value"), existing?.NumberValue)),
            _ => (null, null, null)
        };
    }

    private static bool? ParseNullableBool(string? value, bool? fallback)
    {
        if (value is null)
        {
            return fallback;
        }
        if (string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        if (bool.TryParse(value, out var parsed))
        {
            return parsed;
        }
        throw new CliException($"Invalid boolean value '{value}'. Use true, false, or null.");
    }

    private static double? ParseNullableDouble(string? value, double? fallback)
    {
        if (value is null)
        {
            return fallback;
        }
        if (string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }
        throw new CliException($"Invalid number value '{value}'. Provide a numeric value or 'null'.");
    }

    private static async Task<int> HandleMutationResponse(CliContext context, HttpResponseMessage response, string successMessage, string action)
    {
        if (response.IsSuccessStatusCode)
        {
            await context.Out.WriteLineAsync(successMessage);
            return 0;
        }

        var details = await response.Content.ReadAsStringAsync();
        await context.Error.WriteLineAsync($"Failed to {action} flag: {(int)response.StatusCode} {response.ReasonPhrase}");
        if (!string.IsNullOrWhiteSpace(details))
        {
            await context.Error.WriteLineAsync(details);
        }
        return 1;
    }

    private static async Task<int> FlagDeleteAsync(CliContext context, string[] args)
    {
        if (args.Length == 0)
        {
            await context.Error.WriteLineAsync("Usage: ffaas-admin flags delete <key>");
            return 1;
        }

        var key = args[0];
        var response = await context.HttpClient.DeleteAsync($"/api/flags/{Uri.EscapeDataString(key)}");
        if (response.IsSuccessStatusCode)
        {
            await context.Out.WriteLineAsync($"Deleted flag '{key}'.");
            return 0;
        }

        await context.Error.WriteLineAsync($"Failed to delete flag '{key}': {(int)response.StatusCode} {response.ReasonPhrase}");
        return 1;
    }
}

internal static class AuditCommands
{
    public static async Task<int> ExecuteAsync(CliContext context, string[] args)
    {
        if (args.Length == 0 || string.Equals(args[0], "list", StringComparison.OrdinalIgnoreCase))
        {
            return await AuditListAsync(context, args.Skip(1).ToArray());
        }

        return await UsagePrinter.UnknownPrimaryAsync(context.Out, $"audits {args[0]}");
    }

    private static async Task<int> AuditListAsync(CliContext context, string[] args)
    {
        var options = OptionDictionary.Parse(args);
        var take = ParseTake(options.TryGet("take"), 50);

        var response = await context.HttpClient.GetAsync("/api/audit");
        if (!response.IsSuccessStatusCode)
        {
            await context.Error.WriteLineAsync($"Failed to list audit entries: {(int)response.StatusCode} {response.ReasonPhrase}");
            return 1;
        }

        var payload = await response.Content.ReadAsStringAsync();
        var entries = JsonSerializer.Deserialize<List<AuditRecord>>(payload, context.Json) ?? new List<AuditRecord>();

        var limited = entries.Take(take).ToList();
        if (limited.Count == 0)
        {
            await context.Out.WriteLineAsync("No audit entries found.");
            return 0;
        }

        var table = TableBuilder.Build(
            new[] { "At", "Action", "Flag", "Actor" },
            limited.Select(e => new[]
            {
                e.At.ToString("u"),
                e.Action,
                e.FlagKey ?? "-",
                e.Actor ?? "system"
            }));

        await context.Out.WriteLineAsync(table);
        return 0;
    }

    private static int ParseTake(string? value, int fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }
        if (int.TryParse(value, out var parsed) && parsed > 0)
        {
            return parsed;
        }
        throw new CliException($"Invalid --take value '{value}'. Provide a positive integer.");
    }
}

internal readonly record struct FlagCreateDto(
    string Key,
    FlagType Type,
    bool? BoolValue,
    string? StringValue,
    double? NumberValue,
    List<TargetRule>? Rules);

internal readonly record struct FlagUpdateDto(
    FlagType Type,
    bool? BoolValue,
    string? StringValue,
    double? NumberValue,
    List<TargetRule>? Rules,
    DateTimeOffset LastKnownUpdatedAt);

internal sealed record FlagRecord
{
    public string Key { get; init; } = string.Empty;
    public FlagType Type { get; init; }
    public bool? BoolValue { get; init; }
    public string? StringValue { get; init; }
    public double? NumberValue { get; init; }
    public List<TargetRule> Rules { get; init; } = new();
    public DateTimeOffset UpdatedAt { get; init; }
}

internal sealed record AuditRecord
{
    public string Action { get; init; } = string.Empty;
    public string? FlagKey { get; init; }
    public string? Actor { get; init; }
    public DateTimeOffset At { get; init; }
}

internal sealed class OptionDictionary
{
    private readonly Dictionary<string, string?> _values;

    private OptionDictionary(Dictionary<string, string?> values)
    {
        _values = values;
    }

    public static OptionDictionary Parse(IEnumerable<string> args)
    {
        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var argArray = args.ToArray();
        for (var i = 0; i < argArray.Length; i++)
        {
            var current = argArray[i];
            if (!current.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = current[2..];
            string? value = null;
            if (i + 1 < argArray.Length && !argArray[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = argArray[++i];
            }
            dict[key] = value;
        }
        return new OptionDictionary(dict);
    }

    public string? TryGet(string key)
        => _values.TryGetValue(key, out var value) ? value : null;
}

internal static class TableBuilder
{
    public static string Build(string[] headers, IEnumerable<string[]> rows)
    {
        var data = rows.ToList();
        var widths = new int[headers.Length];
        for (var i = 0; i < headers.Length; i++)
        {
            widths[i] = headers[i].Length;
        }
        foreach (var row in data)
        {
            for (var i = 0; i < headers.Length && i < row.Length; i++)
            {
                widths[i] = Math.Max(widths[i], row[i]?.Length ?? 0);
            }
        }

        var sb = new StringBuilder();
        AppendRow(sb, headers, widths);
        AppendSeparator(sb, widths);
        foreach (var row in data)
        {
            AppendRow(sb, row, widths);
        }
        return sb.ToString();
    }

    private static void AppendRow(StringBuilder sb, string[] values, int[] widths)
    {
        for (var i = 0; i < widths.Length; i++)
        {
            var cell = i < values.Length ? values[i] ?? string.Empty : string.Empty;
            sb.Append(cell.PadRight(widths[i]));
            if (i < widths.Length - 1)
            {
                sb.Append("  ");
            }
        }
        sb.AppendLine();
    }

    private static void AppendSeparator(StringBuilder sb, int[] widths)
    {
        for (var i = 0; i < widths.Length; i++)
        {
            sb.Append(new string('-', widths[i]));
            if (i < widths.Length - 1)
            {
                sb.Append("  ");
            }
        }
        sb.AppendLine();
    }
}

internal static class JsonOptions
{
    public static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

internal sealed class CliException : Exception
{
    public CliException(string message, string? detail = null) : base(message)
    {
        Detail = detail;
    }

    public string? Detail { get; }
}
