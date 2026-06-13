namespace TokenStack.Core.Status;

/// <summary>Renders the exact one-line session status (format-compatible with the
/// retired ensure-stack.ps1 so users see a familiar line). A toggled-OFF layer reads OFF.</summary>
public static class StatusLine
{
    public static string Build(StackStatus s)
    {
        string headroom;
        if (!s.HeadroomEnabled) headroom = "OFF";
        else if (!s.TaskRunning) headroom = "DOWN";
        else if (!s.PortListening) headroom = "starting (cold-load ~50s)";
        else
        {
            var route = s.Routed ? "ROUTED" : "BYPASSED";
            var reqs = s.Reqs is { } n ? $", reqs={n}" : "";
            headroom = $"up (:{s.Port}, {route}{reqs})";
        }
        var rtk = !s.RtkEnabled ? "OFF" : s.RtkOnPath ? "up" : "MISSING";
        var semble = !s.SembleEnabled ? "OFF" : s.SembleWired ? "up (MCP)" : "MISSING";
        return $"[token-stack] Headroom: {headroom} | RTK: {rtk} | Semble: {semble}";
    }
}
