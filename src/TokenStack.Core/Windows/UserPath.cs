namespace TokenStack.Core.Windows;

public static class UserPath
{
    public static bool EnsureContains(IEnvStore env, string dir)
    {
        var entries = Split(env.GetUser("Path"));
        if (entries.Any(e => Same(e, dir))) return false;
        entries.Add(dir.TrimEnd('\\'));
        env.SetUser("Path", string.Join(';', entries));
        return true;
    }

    public static bool Remove(IEnvStore env, string dir)
    {
        var entries = Split(env.GetUser("Path"));
        var kept = entries.Where(e => !Same(e, dir)).ToList();
        if (kept.Count == entries.Count) return false;
        env.SetUser("Path", kept.Count == 0 ? null : string.Join(';', kept));
        return true;
    }

    private static List<string> Split(string? path) =>
        (path ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();

    private static bool Same(string a, string b) =>
        string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
}
