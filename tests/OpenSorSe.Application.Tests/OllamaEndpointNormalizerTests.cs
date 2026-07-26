using OpenSorSe.AI;

namespace OpenSorSe.Application.Tests;

/// <summary>Verifies one deterministic Ollama base endpoint is shared by every transport operation.</summary>
public sealed class OllamaEndpointNormalizerTests
{
    /// <summary>Verifies known API suffixes are removed without losing a supported reverse-proxy base path.</summary>
    [Theory]
    [InlineData("http://127.0.0.1:11434", "http://127.0.0.1:11434/")]
    [InlineData("http://127.0.0.1:11434/api", "http://127.0.0.1:11434/")]
    [InlineData("http://127.0.0.1:11434/api/tags/", "http://127.0.0.1:11434/")]
    [InlineData("http://127.0.0.1:11434/api/generate", "http://127.0.0.1:11434/")]
    [InlineData("https://example.test/ollama/api/version", "https://example.test/ollama/")]
    [InlineData("https://example.test/base/api/chat", "https://example.test/base/")]
    public void TryNormalize_SupportedEndpoint_ProducesTrailingSlashBase(string input, string expected)
    {
        var valid = OllamaEndpointNormalizer.TryNormalize(input, out var endpoint);

        Assert.True(valid);
        Assert.Equal(expected, endpoint.AbsoluteUri);
    }

    /// <summary>Verifies credentials and request-specific URI data are rejected before communication.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("file:///tmp/ollama")]
    [InlineData("http://user:secret@localhost:11434")]
    [InlineData("http://localhost:11434/?token=secret")]
    [InlineData("http://localhost:11434/#fragment")]
    public void TryNormalize_UnsafeOrUnsupportedEndpoint_IsRejected(string input)
    {
        Assert.False(OllamaEndpointNormalizer.TryNormalize(input, out _));
    }
}
