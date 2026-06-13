using System.Text.Json;
using System.Text.Json.Nodes;

namespace TokenStack.Core.Claude;

/// <summary>Load/backup/save wrapper for a Claude config file. Backup is written
/// only when saving, as a timestamped sibling of the original content.</summary>
public sealed class ClaudeFileEditor
{
    private readonly string _path;
    private readonly Func<DateTimeOffset> _clock;
    private string? _originalText;

    public ClaudeFileEditor(string path, Func<DateTimeOffset>? clock = null)
    {
        _path = path;
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public JsonNode Load()
    {
        if (!File.Exists(_path)) { _originalText = null; return new JsonObject(); }
        _originalText = File.ReadAllText(_path);
        return JsonNode.Parse(_originalText) ?? new JsonObject();
    }

    public void SaveWithBackup(JsonNode root)
    {
        if (_originalText is not null)
        {
            var stamp = _clock().ToString("yyyyMMdd-HHmmss");
            File.WriteAllText($"{_path}.token-stack-backup-{stamp}", _originalText);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
