using System.Text.Json;
using System.Text.Json.Nodes;

namespace TokenStack.Core.Config;

public static class ConfigStore
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "token-stack", "config.json");

    public static StackConfig Load(string path)
    {
        var node = JsonNode.Parse(File.ReadAllText(path))
                   ?? throw new InvalidDataException($"Empty config: {path}");
        return node.Deserialize<StackConfig>(Opts)
               ?? throw new InvalidDataException($"Unreadable config: {path}");
    }

    public static void Save(StackConfig cfg, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(cfg, Opts));
    }

    /// <summary>Dot-path read straight from the file (preserving-unknown-keys path).</summary>
    public static string GetValue(string path, string key)
    {
        var node = JsonNode.Parse(File.ReadAllText(path))!;
        var target = Walk(node, key) ?? throw new ArgumentException($"Unknown config key: {key}");
        return target.ToJsonString().Trim('"');
    }

    /// <summary>Dot-path write. Validates the key exists in the schema, preserves unknown keys,
    /// and validates the resulting config before saving.</summary>
    public static void SetValue(string path, string key, string value)
    {
        var root = JsonNode.Parse(File.ReadAllText(path))!;
        var existing = Walk(root, key) ?? throw new ArgumentException($"Unknown config key: {key}");

        JsonNode newNode = existing.GetValueKind() switch
        {
            JsonValueKind.Number => JsonValue.Create(long.Parse(value)),
            JsonValueKind.True or JsonValueKind.False => JsonValue.Create(bool.Parse(value)),
            JsonValueKind.Array => JsonNode.Parse(value)!,
            _ => JsonValue.Create(value)!,
        };

        var parts = key.Split('.');
        var parent = parts.Length == 1 ? root : Walk(root, string.Join('.', parts[..^1]))!;
        parent.AsObject()[parts[^1]] = newNode;

        var candidate = root.Deserialize<StackConfig>(Opts)!;
        var errors = ConfigValidator.Validate(candidate);
        if (errors.Count > 0)
            throw new ArgumentException($"Invalid value for {key}: {string.Join("; ", errors)}");

        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static JsonNode? Walk(JsonNode root, string dotPath)
    {
        JsonNode? cur = root;
        foreach (var part in dotPath.Split('.'))
        {
            cur = cur?.AsObject().TryGetPropertyValue(part, out var next) == true ? next : null;
            if (cur is null) return null;
        }
        return cur;
    }
}
