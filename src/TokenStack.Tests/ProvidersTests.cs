using TokenStack.Core.Components;
using Xunit;

namespace TokenStack.Tests;

public class ProvidersTests
{
    [Theory]
    [InlineData("", "Anthropic")]
    [InlineData("https://api.anthropic.com", "Anthropic")]
    [InlineData("https://api.z.ai/api/anthropic", "GLM")]
    [InlineData("https://api.moonshot.ai/anthropic", "Kimi")]
    [InlineData("https://api.moonshot.cn/anthropic", "Kimi")]
    [InlineData("https://api.minimax.io/anthropic", "MiniMax")]
    [InlineData("https://api.minimaxi.com/anthropic", "MiniMax")]
    [InlineData("https://openrouter.ai/api", "OpenRouter")]
    [InlineData("https://some.other.host/anthropic", "Custom")]
    public void Label_MapsHostToVendor(string url, string expected) =>
        Assert.Equal(expected, Providers.Label(url));

    [Fact]
    public void ResolveUpstream_VendorBaseUrl_IsAdopted() =>
        Assert.Equal("https://api.moonshot.ai/anthropic",
            ProviderDetection.ResolveUpstream("https://api.moonshot.ai/anthropic", "", "http://127.0.0.1:8787"));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("https://api.anthropic.com")]
    public void ResolveUpstream_AnthropicOrEmpty_IsDefault(string? baseUrl) =>
        Assert.Equal("", ProviderDetection.ResolveUpstream(baseUrl, "", "http://127.0.0.1:8787"));

    [Fact]
    public void ResolveUpstream_AlreadyProxy_PreservesExistingUpstream() =>
        Assert.Equal("https://api.z.ai/api/anthropic",
            ProviderDetection.ResolveUpstream("http://127.0.0.1:8787", "https://api.z.ai/api/anthropic", "http://127.0.0.1:8787"));
}
