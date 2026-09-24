using AutoPartsERP.Application.Common.Abstractions;
using Microsoft.Extensions.Configuration;

namespace AutoPartsERP.Infrastructure.Services;

/// <summary>
/// Links into the web application: <c>App:PublicUrl</c> when set, otherwise the first of <c>AllowedOrigins</c> (the address the
/// browser uses, e.g. https://130.94.45.230). Without either, the path alone.
/// </summary>
public sealed class AppLinks : IAppLinks
{
    private readonly string _base;

    public AppLinks(IConfiguration configuration)
    {
        var configured = configuration["App:PublicUrl"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = (configuration["AllowedOrigins"] ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
        }

        _base = (configured ?? string.Empty).TrimEnd('/');
    }

    public string To(string path) => $"{_base}/{path.TrimStart('/')}";
}
