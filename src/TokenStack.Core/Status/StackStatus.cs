namespace TokenStack.Core.Status;

/// <summary>Snapshot of live stack health. Routed = the CURRENT process env points at the proxy.</summary>
public sealed record StackStatus(
    bool TaskRunning,
    bool PortListening,
    bool Routed,
    long? Reqs,
    int Port,
    bool RtkOnPath,
    bool SembleWired);
