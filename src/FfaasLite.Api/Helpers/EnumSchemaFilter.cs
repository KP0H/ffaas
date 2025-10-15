using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace FfaasLite.Api.Helpers
{
    public class EnumSchemaFilter : ISchemaFilter
    {
        public void Apply(OpenApiSchema schema, SchemaFilterContext context)
        {
            if (context.Type.IsEnum)
            {
                schema.Enum.Clear();
                foreach (var name in Enum.GetNames(context.Type))
                {
                    var camel = char.ToLowerInvariant(name[0]) + name[1..];
                    schema.Enum.Add(new Microsoft.OpenApi.Any.OpenApiString(camel));
                }
                schema.Type = "string";
                schema.Format = null;
            }
        }
    }
}
