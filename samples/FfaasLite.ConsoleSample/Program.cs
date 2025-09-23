using FfaasLite.Core.Flags;
using FfaasLite.SDK;

var client = new FlagClient("http://localhost:8080");

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
Console.ReadKey();