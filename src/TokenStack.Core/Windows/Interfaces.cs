namespace TokenStack.Core.Windows;

public sealed record ProcResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Ok => ExitCode == 0;
}

public interface IProcessRunner
{
    ProcResult Run(string file, string args, int timeoutMs = 120000);
}

public interface IEnvStore
{
    string? GetUser(string name);
    void SetUser(string name, string? value);   // null = delete
    string? GetProcess(string name);
}

public interface IPortProbe
{
    bool IsListening(int port);
}

public interface IHttpProbe
{
    /// <summary>GET url. Returns the body on 2xx, null on any error/timeout.</summary>
    string? Get(string url, int timeoutMs = 5000);
}
