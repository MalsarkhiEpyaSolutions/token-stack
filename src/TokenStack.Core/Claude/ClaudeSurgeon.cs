using System.Text.Json.Nodes;
using TokenStack.Core.Components;

namespace TokenStack.Core.Claude;

/// <summary>Pure surgical operations on Claude config JSON trees. Every method returns
/// true when it changed the tree. Identification of "our" entries is by content
/// signature (command contains rtk.exe / token-stack.exe / the legacy scripts) — never
/// by array position, so user-owned entries are preserved.</summary>
public static class ClaudeSurgeon
{
    private static readonly string[] LegacySessionMarkers =
        { "ensure-stack.ps1", "headroom.exe\" init hook ensure", "token-stack.exe\" status --hook" };

    // ---------- RTK PreToolUse ----------

    public static bool EnsureRtkHook(JsonNode root, string rtkExePath, string matcher)
    {
        var wanted = $"\"{rtkExePath}\" hook claude";
        var pre = GetOrCreateArray(root, "hooks", "PreToolUse");
        var ours = pre.Where(IsRtkEntry).ToList();

        if (ours.Count == 1
            && ours[0]!["matcher"]?.GetValue<string>() == matcher
            && ours[0]!["hooks"]![0]!["command"]!.GetValue<string>() == wanted)
            return false;

        foreach (var o in ours) pre.Remove(o);
        pre.Add(new JsonObject
        {
            ["matcher"] = matcher,
            ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = wanted }),
        });
        return true;
    }

    public static bool RemoveRtkHook(JsonNode root)
    {
        var pre = root["hooks"]?["PreToolUse"]?.AsArray();
        if (pre is null) return false;
        var ours = pre.Where(IsRtkEntry).ToList();
        foreach (var o in ours) pre.Remove(o);
        return ours.Count > 0;
    }

    private static bool IsRtkEntry(JsonNode? entry) =>
        FirstCommand(entry)?.Contains("rtk.exe", StringComparison.OrdinalIgnoreCase) == true;

    // ---------- SessionStart status hook ----------

    public static bool EnsureSessionStatusHook(JsonNode root, string exePath)
    {
        var wanted = $"\"{exePath}\" status --hook";
        var ss = GetOrCreateArray(root, "hooks", "SessionStart");
        var ours = ss.Where(IsOurSessionEntry).ToList();

        if (ours.Count == 1 && FirstCommand(ours[0]) == wanted)
            return false;

        foreach (var o in ours) ss.Remove(o);
        ss.Add(new JsonObject
        {
            ["matcher"] = "startup|resume",
            ["hooks"] = new JsonArray(new JsonObject
            {
                ["type"] = "command",
                ["command"] = wanted,
                ["timeout"] = 30,
                ["statusMessage"] = "Checking token stack (Headroom/RTK/Semble)...",
            }),
        });
        return true;
    }

    public static bool RemoveSessionStatusHook(JsonNode root)
    {
        var ss = root["hooks"]?["SessionStart"]?.AsArray();
        if (ss is null) return false;
        var ours = ss.Where(IsOurSessionEntry).ToList();
        foreach (var o in ours) ss.Remove(o);
        return ours.Count > 0;
    }

    private static bool IsOurSessionEntry(JsonNode? entry)
    {
        var cmd = FirstCommand(entry);
        return cmd is not null && (LegacySessionMarkers.Any(m =>
            cmd.Contains(m, StringComparison.OrdinalIgnoreCase))
            || cmd.Contains(Branding.ExeName, StringComparison.OrdinalIgnoreCase)
            || cmd.Contains(Branding.LegacyExeName, StringComparison.OrdinalIgnoreCase));
    }

    // ---------- env.ANTHROPIC_BASE_URL ----------

    public static bool SetEnvBaseUrl(JsonNode root, string url)
    {
        var env = root["env"]?.AsObject();
        if (env is null) { env = new JsonObject(); root.AsObject()["env"] = env; }
        if (env["ANTHROPIC_BASE_URL"]?.GetValue<string>() == url) return false;
        env["ANTHROPIC_BASE_URL"] = url;
        return true;
    }

    public static bool RemoveEnvBaseUrl(JsonNode root)
    {
        var env = root["env"]?.AsObject();
        if (env is null || !env.ContainsKey("ANTHROPIC_BASE_URL")) return false;
        env.Remove("ANTHROPIC_BASE_URL");
        if (env.Count == 0) root.AsObject().Remove("env");
        return true;
    }

    // ---------- mcpServers.semble ----------

    public static bool EnsureSembleMcp(JsonNode root, string sembleExePath)
    {
        var servers = root["mcpServers"]?.AsObject();
        if (servers is null) { servers = new JsonObject(); root.AsObject()["mcpServers"] = servers; }

        var existing = servers["semble"];
        if (existing?["command"]?.GetValue<string>() == sembleExePath
            && existing["type"]?.GetValue<string>() == "stdio")
            return false;

        servers["semble"] = new JsonObject
        {
            ["type"] = "stdio",
            ["command"] = sembleExePath,
            ["args"] = new JsonArray(),
            ["env"] = new JsonObject(),
        };
        return true;
    }

    public static bool RemoveSembleMcp(JsonNode root)
    {
        var servers = root["mcpServers"]?.AsObject();
        if (servers is null || !servers.ContainsKey("semble")) return false;
        servers.Remove("semble");
        return true;
    }

    // ---------- helpers ----------

    private static string? FirstCommand(JsonNode? entry) =>
        entry?["hooks"]?.AsArray().FirstOrDefault()?["command"]?.GetValue<string>();

    private static JsonArray GetOrCreateArray(JsonNode root, string parent, string key)
    {
        var p = root[parent]?.AsObject();
        if (p is null) { p = new JsonObject(); root.AsObject()[parent] = p; }
        var arr = p[key]?.AsArray();
        if (arr is null) { arr = new JsonArray(); p[key] = arr; }
        return arr;
    }
}
