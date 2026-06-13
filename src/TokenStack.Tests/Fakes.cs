using TokenStack.Core.Windows;

namespace TokenStack.Tests;

public sealed class FakeRunner : IProcessRunner
{
    public readonly List<string> Calls = new();
    public Func<string, string, ProcResult> Handler { get; set; } =
        (_, _) => new ProcResult(0, "", "");

    public ProcResult Run(string file, string args, int timeoutMs = 120000)
    {
        Calls.Add($"{file} {args}".Trim());
        return Handler(file, args);
    }
}

public sealed class FakeEnv : IEnvStore
{
    public readonly Dictionary<string, string> User = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, string> Process = new(StringComparer.OrdinalIgnoreCase);

    public string? GetUser(string name) => User.TryGetValue(name, out var v) ? v : null;
    public void SetUser(string name, string? value)
    {
        if (value is null) User.Remove(name); else User[name] = value;
    }
    public string? GetProcess(string name) => Process.TryGetValue(name, out var v) ? v : null;
}

public sealed class FakePort : IPortProbe
{
    public bool Listening { get; set; }
    public bool IsListening(int port) => Listening;
}

public sealed class FakeHttp : IHttpProbe
{
    public Dictionary<string, string> Responses { get; } = new(); // url → body ("" = 200 empty)
    public string? Get(string url, int timeoutMs = 5000) =>
        Responses.TryGetValue(url, out var body) ? body : null;   // null = unreachable
}
