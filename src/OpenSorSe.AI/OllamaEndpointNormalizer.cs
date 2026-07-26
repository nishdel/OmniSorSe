namespace OpenSorSe.AI;

/// <summary>Normalizes supported user-entered Ollama endpoints for all transport operations.</summary>
public static class OllamaEndpointNormalizer
{
    private static readonly string[] KnownApiSuffixes =
    [
        "/api/generate",
        "/api/chat",
        "/api/tags",
        "/api/version",
        "/api",
    ];

    /// <summary>Creates one credential-free trailing-slash base URI shared by every Ollama request.</summary>
    public static bool TryNormalize(string? endpoint, out Uri baseEndpoint)
    {
        baseEndpoint = default!;
        if (!Uri.TryCreate(endpoint?.Trim(), UriKind.Absolute, out var parsed) ||
            parsed.Scheme is not ("http" or "https") ||
            string.IsNullOrWhiteSpace(parsed.Host) ||
            !string.IsNullOrEmpty(parsed.UserInfo) ||
            !string.IsNullOrEmpty(parsed.Query) ||
            !string.IsNullOrEmpty(parsed.Fragment))
        {
            return false;
        }

        var path = parsed.AbsolutePath.TrimEnd('/');
        foreach (var suffix in KnownApiSuffixes)
        {
            if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                path = path[..^suffix.Length].TrimEnd('/');
                break;
            }
        }

        var builder = new UriBuilder(parsed.Scheme, parsed.Host, parsed.IsDefaultPort ? -1 : parsed.Port)
        {
            Path = string.IsNullOrEmpty(path) ? "/" : $"{path}/",
            Query = string.Empty,
            Fragment = string.Empty,
            UserName = string.Empty,
            Password = string.Empty,
        };
        baseEndpoint = builder.Uri;
        return true;
    }
}
