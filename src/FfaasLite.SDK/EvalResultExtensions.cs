using FfaasLite.Core.Flags;
using FfaasLite.Core.Models;

namespace FfaasLite.SDK
{
    public static class EvalResultExtensions
    {
        /// <summary>
        /// Try take boolean from EvalResult.
        /// </summary>
        public static bool? AsBool(this EvalResult r)
            => r.Type == FlagType.Boolean ? r.Value as bool? : null;

        /// <summary>
        /// Try take string from EvalResult.
        /// </summary>
        public static string? AsString(this EvalResult r)
            => r.Type == FlagType.String ? r.Value as string : null;

        /// <summary>
        /// Try take number (double) from EvalResult.
        /// </summary>
        public static double? AsNumber(this EvalResult r)
            => r.Type == FlagType.Number ? r.Value as double? : null;
    }
}
