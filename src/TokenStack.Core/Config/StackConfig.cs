using System.Text.Json.Serialization;

namespace TokenStack.Core.Config;

public sealed class StackConfig
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = 1;
    [JsonPropertyName("installRoot")] public string InstallRoot { get; set; } = "";
    [JsonPropertyName("headroom")] public HeadroomConfig Headroom { get; set; } = new();
    [JsonPropertyName("rtk")] public RtkConfig Rtk { get; set; } = new();
    [JsonPropertyName("semble")] public SembleConfig Semble { get; set; } = new();
    [JsonPropertyName("routing")] public RoutingConfig Routing { get; set; } = new();
    [JsonPropertyName("hooks")] public HooksConfig Hooks { get; set; } = new();
    [JsonPropertyName("bootstrap")] public BootstrapConfig Bootstrap { get; set; } = new();

    public static StackConfig CreateDefault(string installRoot) => new() { InstallRoot = installRoot };
}

public sealed class HeadroomConfig
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("port")] public int Port { get; set; } = 8787;
    [JsonPropertyName("mode")] public string Mode { get; set; } = "token";
    [JsonPropertyName("version")] public string Version { get; set; } = "0.30.0";
    [JsonPropertyName("pythonVersion")] public string PythonVersion { get; set; } = "3.12";
    [JsonPropertyName("extraArgs")] public List<string> ExtraArgs { get; set; } = new();
}

public sealed class RtkConfig
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("version")] public string Version { get; set; } = "0.42.3";
    [JsonPropertyName("source")] public string Source { get; set; } = "github:rtk-ai/rtk";
    [JsonPropertyName("hookMatcher")] public string HookMatcher { get; set; } = "Bash";
}

public sealed class SembleConfig
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("version")] public string Version { get; set; } = "latest";
}

public sealed class RoutingConfig
{
    [JsonPropertyName("cli")] public bool Cli { get; set; } = true;
    [JsonPropertyName("desktop")] public bool Desktop { get; set; } = true;
}

public sealed class HooksConfig
{
    [JsonPropertyName("sessionStatusLine")] public bool SessionStatusLine { get; set; } = true;
}

public sealed class BootstrapConfig
{
    [JsonPropertyName("uvInstaller")] public string UvInstaller { get; set; } = "auto";
}
