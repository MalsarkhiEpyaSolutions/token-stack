using TokenStack.Core.Config;

namespace TokenStack.Core.Components;

/// <summary>The independently-toggleable layers, plus All (the desktop button).</summary>
public enum StackLayer { All, Headroom, Rtk, Semble, Cco }

/// <summary>Pure on/off flag logic over the config's `enabled` flags (the toggle state of
/// record). Wiring the change into Claude/Windows is <see cref="ToggleService"/>'s job.</summary>
public static class ToggleLogic
{
    public static StackLayer ParseLayer(string? s) => (s ?? "all").Trim().ToLowerInvariant() switch
    {
        "all" or "" => StackLayer.All,
        "headroom" => StackLayer.Headroom,
        "rtk" => StackLayer.Rtk,
        "semble" => StackLayer.Semble,
        "cco" => StackLayer.Cco,
        _ => throw new ArgumentException($"unknown layer '{s}' — use headroom | rtk | semble | cco | all"),
    };

    /// <summary>Is the layer currently on? All = on when ANY layer is on (so the button's
    /// "toggle" turns everything off when anything is running, else turns everything on).</summary>
    public static bool CurrentlyOn(StackConfig c, StackLayer l) => l switch
    {
        StackLayer.Headroom => c.Headroom.Enabled,
        StackLayer.Rtk => c.Rtk.Enabled,
        StackLayer.Semble => c.Semble.Enabled,
        StackLayer.Cco => c.Cco.Enabled,
        _ => c.Headroom.Enabled || c.Rtk.Enabled || c.Semble.Enabled || c.Cco.Enabled,
    };

    public static void SetFlag(StackConfig c, StackLayer l, bool on)
    {
        if (l is StackLayer.All or StackLayer.Headroom) c.Headroom.Enabled = on;
        if (l is StackLayer.All or StackLayer.Rtk) c.Rtk.Enabled = on;
        if (l is StackLayer.All or StackLayer.Semble) c.Semble.Enabled = on;
        if (l is StackLayer.All or StackLayer.Cco) c.Cco.Enabled = on;
    }

    public static void Flip(StackConfig c, StackLayer l) => SetFlag(c, l, !CurrentlyOn(c, l));
}
