namespace TokenStack.Core.Components;

public static class FsUtil
{
    /// <summary>Recursively copy a directory tree (relative-path based, so it's safe when the
    /// source path string also appears elsewhere). Overwrites existing files.</summary>
    public static void CopyDirectory(string src, string dst)
    {
        if (!Directory.Exists(src))
            throw new DirectoryNotFoundException($"source directory not found: {src}");
        Directory.CreateDirectory(dst);
        foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(dst, Path.GetRelativePath(src, dir)));
        foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(dst, Path.GetRelativePath(src, file)), overwrite: true);
    }
}
