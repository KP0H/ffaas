using FfaasLite.Core.Flags;
using FfaasLite.SDK;

var client = new FlagClient("http://localhost:8080");
// Tip: for admin requests (POST/PUT/DELETE /api/flags) include an API key header such as "Authorization: Bearer dev-editor-token".

var ctx = new EvalContext(
    UserId: "u3",
    Attributes: new Dictionary<string, string>
    {
        ["country"] = "NL",
        ["email"] = "test@corp.com"
    }
);

var result = await client.EvaluateAsync("ui-ver", ctx);

Console.WriteLine($"Flag '{result.Key}' evaluated:");
Console.WriteLine($"  Value   : {result.Value}");
Console.WriteLine($"  Variant : {result.Variant}");
Console.WriteLine($"  Type    : {result.Type}");

result = await client.EvaluateAsync("new-ui", ctx);
Console.WriteLine($"Flag '{result.Key}' evaluated:");
if (result.AsBool() == true)
{
    Console.WriteLine("New UI enabled!");
}
else
{
    Console.WriteLine("Using old UI");
}

var rateLimit = await client.EvaluateAsync("rate-limit", ctx);
Console.WriteLine($"Flag '{rateLimit.Key}' evaluated:");
Console.WriteLine($"  Value   : {rateLimit.Value}");
Console.WriteLine($"  Variant : {rateLimit.Variant}");
Console.WriteLine($"  Type    : {rateLimit.Type}");
Console.ReadKey();
