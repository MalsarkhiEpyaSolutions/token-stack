using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using TokenStack.Core.Config;
using TokenStack.Core.Windows;

namespace TokenStack.Core.Components;

/// <summary>The read-cache layer: extracts the pinned CCO snapshot embedded in this assembly,
/// disables the lossy big-file "map-then-load" nudge, and smoke-tests that Node can load the
/// ESM module graph. Only read-cache.js is ever wired (see ClaudeSurgeon.EnsureCcoHooks).</summary>
public sealed class CcoComponent(IProcessRunner runner)
{
    public const string ResourceName = "TokenStack.Core.cco.zip";

    public static string CcoDir(StackConfig cfg) => Path.Combine(cfg.InstallRoot, "cco");
    public static string ReadCacheJs(StackConfig cfg) => Path.Combine(CcoDir(cfg), "src", "read-cache.js");

    private static string DataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude-context-optimizer");

    /// <summary>Unpack the embedded CCO snapshot into destDir (creates destDir/src/... etc.).</summary>
    public static void ExtractSnapshot(string destDir)
    {
        Directory.CreateDirectory(destDir);
        using var stream = typeof(CcoComponent).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"embedded resource '{ResourceName}' not found");
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        zip.ExtractToDirectory(destDir, overwriteFiles: true);
    }

    /// <summary>Merge {"bigFileDigest": false} into CCO's config JSON, preserving other keys.</summary>
    public static string DisableBigFileDigest(string? existingJson)
    {
        JsonObject obj;
        try { obj = (JsonNode.Parse(string.IsNullOrWhiteSpace(existingJson) ? "{}" : existingJson) as JsonObject) ?? new(); }
        catch { obj = new(); }
        obj["bigFileDigest"] = false;
        return obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public bool NodePresent() => runner.Run("node", "--version", 15000).Ok;

    /// <summary>Extract the snapshot, disable the nudge, smoke-test the ESM graph. Caller must
    /// have confirmed NodePresent() first (this throws if the smoke fails).</summary>
    public void Install(StackConfig cfg)
    {
        ExtractSnapshot(CcoDir(cfg));

        Directory.CreateDirectory(DataDir);
        var configPath = Path.Combine(DataDir, "config.json");
        var existing = File.Exists(configPath) ? File.ReadAllText(configPath) : null;
        File.WriteAllText(configPath, DisableBigFileDigest(existing));

        // Smoke: importing read-cache.js (not as main) loads utils/contextignore/file-digest
        // without running the hook body — proves the vendored ESM graph resolves under this Node.
        var smoke = Path.Combine(CcoDir(cfg), "_smoke.mjs");
        File.WriteAllText(smoke, "import './src/read-cache.js';\n");
        try
        {
            var r = runner.Run("node", $"\"{smoke}\"", 20000);
            if (!r.Ok)
                throw new InvalidOperationException($"cco smoke failed (node could not load read-cache.js): {r.StdErr}");
        }
        finally { try { File.Delete(smoke); } catch { /* best effort */ } }
    }
}
