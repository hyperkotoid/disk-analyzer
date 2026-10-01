namespace DiskUsage.Core;

public static class DiskPath
{
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "/";

        path = path.Replace('\\', '/');
        if (path.Length > 1)
            path = path.TrimEnd('/');

        return path.Length == 0 ? "/" : path;
    }

    public static string? Parent(string path)
    {
        path = Normalize(path);
        if (path == "/")
            return null;

        var slash = path.LastIndexOf('/');
        return slash <= 0 ? "/" : path[..slash];
    }

    public static IReadOnlyList<(string Label, string Path)> Crumbs(string path)
    {
        path = Normalize(path);
        var crumbs = new List<(string Label, string Path)> { ("Корень", "/") };
        if (path == "/")
            return crumbs;

        var current = "";
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current += "/" + part;
            crumbs.Add((part, current));
        }

        return crumbs;
    }
}
