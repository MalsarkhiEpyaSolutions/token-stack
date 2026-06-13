using TokenStack.Core.Config;
using TokenStack.Core.Windows;

namespace TokenStack.Cli.Commands;

internal static class Services
{
    public static readonly IProcessRunner Runner = new RealProcessRunner();
    public static readonly IEnvStore Env = new RealEnvStore();
    public static readonly IPortProbe Port = new RealPortProbe();
    public static readonly IHttpProbe Http = new RealHttpProbe();

    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");
    public static string ClaudeJsonPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json");

    public static StackConfig LoadConfigOrDefault()
    {
        var path = ConfigStore.DefaultPath;
        if (File.Exists(path)) return ConfigStore.Load(path);
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "token-stack");
        return StackConfig.CreateDefault(root);
    }
}
