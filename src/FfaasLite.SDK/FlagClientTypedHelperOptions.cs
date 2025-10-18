using System;
using System.Collections.Generic;

namespace FfaasLite.SDK;

public sealed class FlagClientTypedHelperOptions
{
    public IReadOnlyList<string> FlagKeys { get; set; } = Array.Empty<string>();

    public string ClassName { get; set; } = "FlagHelper";

    public string Namespace { get; set; } = "FfaasLite.Generated";
}
