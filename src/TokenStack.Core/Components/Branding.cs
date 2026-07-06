namespace TokenStack.Core.Components;

/// <summary>Central product naming. The on-disk exe is token-saver.exe; token-stack.exe is
/// kept as a LEGACY marker so hooks/shortcuts from v1.0.x installs migrate on upgrade.</summary>
public static class Branding
{
    public const string ExeName = "token-saver.exe";
    public const string LegacyExeName = "token-stack.exe";
}
