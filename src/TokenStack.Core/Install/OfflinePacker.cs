using System.IO.Compression;
using TokenStack.Core.Components;
using TokenStack.Core.Config;
using TokenStack.Core.Windows;
using static TokenStack.Core.Components.Quoting;

namespace TokenStack.Core.Install;

/// <summary>Builds the offline `vendor\` bundle on an ONLINE machine: portable python +
/// wheelhouse + uv.exe + rtk.exe + HuggingFace model cache + token-stack.exe, zipped for
/// hand-off to air-gapped machines. Requires a prior online install (so rtk.exe + the HF
/// models already exist locally).</summary>
public static class OfflinePacker
{
    /// <summary>pip-WHEEL args (after the python exe). `pip wheel` BUILDS wheels on this online
    /// machine — not `pip download`, because headroom-ai ships only sdists past 0.20.15 and an
    /// sdist can't be built on an air-gapped target. Specs are pinned to match the online
    /// install so both modes land the identical version.</summary>
    public static string PlanWheelhouseArgs(string wheelhouseDir, string headroomSpec, string sembleSpec) =>
        $"-m pip wheel {headroomSpec} {sembleSpec} -w {Q(wheelhouseDir)}";

    /// <summary>The online machine's HuggingFace cache (populated after the proxy runs once).</summary>
    public static string HfCacheSource() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "huggingface");

    public static void Pack(StackConfig cfg, IProcessRunner runner, string uvExe, string outZip,
        Action<string> log)
    {
        // 1. Locate a managed python that HAS pip (python-build-standalone) via uv.
        var find = runner.Run(uvExe, $"python find {cfg.Headroom.PythonVersion}", 60000);
        if (!find.Ok || string.IsNullOrWhiteSpace(find.StdOut))
            throw new InvalidOperationException(
                $"could not locate a managed python via `uv python find {cfg.Headroom.PythonVersion}` — " +
                $"run `uv python install {cfg.Headroom.PythonVersion}` first.");
        var basePython = find.StdOut.Trim().Split('\n', '\r').First(s => s.Length > 0).Trim();
        var basePythonDir = Path.GetDirectoryName(basePython)
            ?? throw new InvalidOperationException($"unexpected python path: {basePython}");

        var staging = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outZip))!, ".ts-offline-staging");
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        var vendor = Path.Combine(staging, "vendor");
        Directory.CreateDirectory(vendor);

        log("  + uv.exe");
        File.Copy(uvExe, Path.Combine(vendor, "uv.exe"), overwrite: true);

        log("  + python (portable, ~61MB)");
        FsUtil.CopyDirectory(basePythonDir, Path.Combine(vendor, "python"));

        log("  + wheelhouse (pip wheel — building pinned headroom + semble wheels)");
        var wheelhouse = Path.Combine(vendor, "wheelhouse");
        var headroomSpec = $"headroom-ai[proxy]=={cfg.Headroom.Version}";
        var sembleSpec = SembleComponent.InstallSpec(cfg.Semble);
        var dl = runner.Run(basePython, PlanWheelhouseArgs(wheelhouse, headroomSpec, sembleSpec), 900000);
        if (!dl.Ok)
            throw new InvalidOperationException($"pip wheel failed: {dl.StdErr}{dl.StdOut}");

        log("  + rtk.exe");
        var rtkSrc = Path.Combine(cfg.InstallRoot, "rtk", "rtk.exe");
        if (!File.Exists(rtkSrc))
            throw new FileNotFoundException(
                $"rtk.exe not found at {rtkSrc} — run an online `token-stack install` first so the bundle can include it.");
        File.Copy(rtkSrc, Path.Combine(vendor, "rtk.exe"), overwrite: true);

        log("  + hf-cache (HuggingFace models, ~222MB)");
        var hfSrc = HfCacheSource();
        if (!Directory.Exists(Path.Combine(hfSrc, "hub")))
            throw new DirectoryNotFoundException(
                $"HuggingFace models not found under {hfSrc} — start the proxy once on this online " +
                "machine (so Headroom downloads its models), then re-run `token-stack pack`.");
        FsUtil.CopyDirectory(hfSrc, Path.Combine(vendor, "hf-cache"));

        log("  + token-stack.exe");
        var self = Environment.ProcessPath
            ?? throw new InvalidOperationException("cannot resolve the running token-stack.exe path");
        File.Copy(self, Path.Combine(staging, Branding.ExeName), overwrite: true);

        log($"  zipping → {outZip}");
        if (File.Exists(outZip)) File.Delete(outZip);
        ZipFile.CreateFromDirectory(staging, outZip, CompressionLevel.Optimal, includeBaseDirectory: false);
        Directory.Delete(staging, recursive: true);
    }
}
