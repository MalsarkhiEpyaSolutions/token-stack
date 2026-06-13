namespace TokenStack.Core.Components;

public static class Quoting
{
    /// <summary>Quote a path argument only when it contains a space (offline bundles may be
    /// unzipped under a spaced path; space-free paths stay bare so command strings read clean).</summary>
    public static string Q(string p) => p.Contains(' ') ? $"\"{p}\"" : p;
}
