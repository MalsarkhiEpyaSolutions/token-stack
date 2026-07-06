namespace TokenStack.Core.Components;

/// <summary>Static vendor table: maps an Anthropic-compatible upstream URL to a display
/// label. Data, not an abstraction — adoption works for ANY non-Anthropic upstream; the
/// table only names the well-known ones.</summary>
public static class Providers
{
    private static readonly (string Fragment, string Label)[] Vendors =
    {
        ("z.ai", "GLM"),
        ("moonshot.ai", "Kimi"),
        ("moonshot.cn", "Kimi"),
        ("minimax.io", "MiniMax"),
        ("minimaxi.com", "MiniMax"),
    };

    public static string Label(string upstreamUrl)
    {
        if (string.IsNullOrWhiteSpace(upstreamUrl)
            || upstreamUrl.Contains("api.anthropic.com", StringComparison.OrdinalIgnoreCase))
            return "Anthropic";
        foreach (var (frag, label) in Vendors)
            if (upstreamUrl.Contains(frag, StringComparison.OrdinalIgnoreCase)) return label;
        return "Custom";
    }
}

/// <summary>Decides the proxy upstream from the user's CURRENT Claude Code base URL.</summary>
public static class ProviderDetection
{
    public static string ResolveUpstream(string? currentBaseUrl, string existingUpstream, string proxyUrl)
    {
        if (string.IsNullOrWhiteSpace(currentBaseUrl)) return "";
        var url = currentBaseUrl.Trim();
        // Already routed through our proxy: keep whatever upstream we adopted before.
        if (url.Equals(proxyUrl, StringComparison.OrdinalIgnoreCase)
            || url.Contains("127.0.0.1", StringComparison.Ordinal)
            || url.Contains("localhost", StringComparison.OrdinalIgnoreCase))
            return existingUpstream;
        // Anthropic first-party = default (no target override).
        if (url.Contains("api.anthropic.com", StringComparison.OrdinalIgnoreCase)) return "";
        // Anything else = a vendor / custom Anthropic-compatible endpoint: adopt it.
        return url;
    }
}
