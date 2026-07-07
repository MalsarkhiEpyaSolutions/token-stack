namespace TokenStack.Core.Status;

/// <summary>Snapshot of live stack health. Routed = the CURRENT process env points at the
/// proxy. The *Enabled flags are the configured on/off (toggle) state; a disabled layer
/// reads OFF in the status line regardless of what's physically present.</summary>
public sealed record StackStatus(
    bool TaskRunning,
    bool PortListening,
    bool Routed,
    long? Reqs,
    int Port,
    bool RtkOnPath,
    bool SembleWired,
    bool HeadroomEnabled,
    bool RtkEnabled,
    bool SembleEnabled,
    string ProviderLabel = "Anthropic",
    bool CcoEnabled = false,
    bool CcoWired = false);
