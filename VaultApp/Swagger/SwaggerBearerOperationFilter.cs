using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace VaultApp.Swagger;

/// <summary>Adds Bearer auth to Swagger for API routes other than login/register.</summary>
public sealed class SwaggerBearerOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var path = context.ApiDescription.RelativePath ?? "";
        if (!path.StartsWith("api/", StringComparison.OrdinalIgnoreCase))
            return;

        if (string.Equals(path, "api/auth/register", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, "api/auth/login", StringComparison.OrdinalIgnoreCase))
            return;

        operation.Security = new List<OpenApiSecurityRequirement>
        {
            new()
            {
                [new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                }] = Array.Empty<string>()
            }
        };
    }
}
