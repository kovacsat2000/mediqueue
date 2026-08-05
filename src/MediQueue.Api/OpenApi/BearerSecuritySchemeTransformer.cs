using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace MediQueue.Api.OpenApi;

/// <summary>
/// Declares the bearer scheme on the OpenAPI document, so the reference UI grows
/// an Authorize control instead of leaving every protected endpoint untestable.
/// </summary>
/// <remarks>
/// .NET 10's <c>AddOpenApi()</c> does not infer security schemes from the
/// authentication configuration, so this has to be said explicitly. Two details
/// break every pre-.NET-10 example: the namespace is
/// <c>Microsoft.OpenApi</c> rather than <c>Microsoft.OpenApi.Models</c>, and the
/// dictionary holds <c>IOpenApiSecurityScheme</c> rather than the concrete type.
/// </remarks>
internal sealed class BearerSecuritySchemeTransformer(
    IAuthenticationSchemeProvider authenticationSchemeProvider) : IOpenApiDocumentTransformer
{
    /// <inheritdoc />
    public async Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        var schemes = await authenticationSchemeProvider.GetAllSchemesAsync().ConfigureAwait(false);

        if (!schemes.Any(scheme => scheme.Name == "Bearer"))
        {
            return;
        }

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
        {
            ["Bearer"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Paste the accessToken returned by POST /api/auth/login.",
            },
        };
    }
}
