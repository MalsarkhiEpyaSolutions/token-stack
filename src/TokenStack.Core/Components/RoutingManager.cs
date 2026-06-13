using System.Text.RegularExpressions;
using TokenStack.Core.Windows;

namespace TokenStack.Core.Components;

public sealed record RoutingDiagnosis(
    bool SessionRouted, bool UserScopeCorrect, bool Conflict, bool ModelPinPresent);

/// <summary>Owns ANTHROPIC_BASE_URL routing. CLI routing (settings.json env) is applied
/// by the pipeline via ClaudeSurgeon.SetEnvBaseUrl; this class owns the User-scope
/// variable (Desktop ignores settings.json env) and the conflict diagnosis.</summary>
public sealed class RoutingManager(IEnvStore env)
{
    public const string BaseUrlVar = "ANTHROPIC_BASE_URL";

    public static string ProxyUrl(int port) => $"http://127.0.0.1:{port}";

    public void ApplyDesktop(int port) => env.SetUser(BaseUrlVar, ProxyUrl(port));
    public void RemoveDesktop() => env.SetUser(BaseUrlVar, null);

    /// <summary>True when THIS process inherited a base URL pointing at the local proxy —
    /// the only honest routing signal (config presence proves nothing).</summary>
    public bool IsSessionRouted(int port) =>
        env.GetProcess(BaseUrlVar) is { } v
        && Regex.IsMatch(v, $@"^https?://(127\.0\.0\.1|localhost):{port}/?$");

    public RoutingDiagnosis Diagnose(int port)
    {
        var sessionRouted = IsSessionRouted(port);
        var userVal = env.GetUser(BaseUrlVar);
        var userCorrect = userVal == ProxyUrl(port);
        return new RoutingDiagnosis(
            SessionRouted: sessionRouted,
            UserScopeCorrect: userCorrect,
            Conflict: userCorrect && !sessionRouted,
            ModelPinPresent: env.GetUser("ANTHROPIC_MODEL") is not null);
    }
}
