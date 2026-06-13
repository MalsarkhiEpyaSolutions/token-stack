namespace TokenStack.Core.Components;

/// <summary>Where the installer gets its bits. Online = download at install time (v1.0
/// behavior). Offline = a `vendor\` bundle (uv.exe + portable python + wheelhouse + rtk.exe
/// + hf-cache) sitting next to token-stack.exe, for air-gapped machines.</summary>
public sealed record InstallSource(bool IsOffline, string VendorDir)
{
    public string Uv => Path.Combine(VendorDir, "uv.exe");
    public string PythonDir => Path.Combine(VendorDir, "python");
    public string Wheelhouse => Path.Combine(VendorDir, "wheelhouse");
    public string RtkExe => Path.Combine(VendorDir, "rtk.exe");
    public string HfCache => Path.Combine(VendorDir, "hf-cache");

    public static readonly InstallSource Online = new(false, "");
}

public static class InstallSourceResolver
{
    /// <summary>forceOffline: true = require a vendor bundle (throw if missing),
    /// false = force online, null = auto-detect (offline iff a vendor bundle is present).
    /// A bundle is recognized by vendor\uv.exe next to the running exe.</summary>
    public static InstallSource Resolve(string exeDir, bool? forceOffline)
    {
        var vendor = Path.Combine(exeDir, "vendor");
        var hasVendor = File.Exists(Path.Combine(vendor, "uv.exe"));

        if (forceOffline == true)
        {
            if (!hasVendor)
                throw new InvalidOperationException(
                    "--offline requested but no offline bundle found. Expected a `vendor\\` " +
                    "folder (with uv.exe, python, wheelhouse, rtk.exe, hf-cache) next to " +
                    $"token-stack.exe at: {vendor}. Build one on an online machine with " +
                    "`token-stack pack`.");
            return new InstallSource(true, vendor);
        }
        if (forceOffline == false)
            return InstallSource.Online;

        return hasVendor ? new InstallSource(true, vendor) : InstallSource.Online;
    }
}
