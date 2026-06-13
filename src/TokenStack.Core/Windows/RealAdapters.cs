using System.Diagnostics;
using System.Net.NetworkInformation;

namespace TokenStack.Core.Windows;

public sealed class RealProcessRunner : IProcessRunner
{
    public ProcResult Run(string file, string args, int timeoutMs = 120000)
    {
        try
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo(file, args)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };
            // uv defaults its managed-Python dir to %APPDATA% (roaming). Corporate roaming
            // profiles corrupt the minor-version junction ("Missing expected target directory
            // for Python minor version link" — seen live on the reference machine), so pin it
            // to local disk unless the user already chose a location.
            if (!p.StartInfo.Environment.ContainsKey("UV_PYTHON_INSTALL_DIR"))
                p.StartInfo.Environment["UV_PYTHON_INSTALL_DIR"] = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "uv", "python");
            p.Start();
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* already gone */ }
                return new ProcResult(-1, "", $"timeout after {timeoutMs}ms: {file} {args}");
            }
            return new ProcResult(p.ExitCode, stdout.Result, stderr.Result);
        }
        catch (Exception ex) // e.g. Win32Exception: file not found
        {
            return new ProcResult(-1, "", ex.Message);
        }
    }
}

public sealed class RealEnvStore : IEnvStore
{
    public string? GetUser(string name) =>
        Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
    public void SetUser(string name, string? value) =>
        Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.User);
    public string? GetProcess(string name) =>
        Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
}

public sealed class RealPortProbe : IPortProbe
{
    public bool IsListening(int port) =>
        IPGlobalProperties.GetIPGlobalProperties()
            .GetActiveTcpListeners().Any(ep => ep.Port == port);
}

public sealed class RealHttpProbe : IHttpProbe
{
    public string? Get(string url, int timeoutMs = 5000)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };
            using var resp = http.GetAsync(url).GetAwaiter().GetResult();
            return resp.IsSuccessStatusCode
                ? resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                : null;
        }
        catch { return null; }
    }
}
