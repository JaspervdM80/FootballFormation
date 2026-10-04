namespace FootballFormation.Core.Tests;

/// For the tests that read the repository's own source and instructions rather than run its code.
internal static class Repository
{
    public static string Root { get; } = FindRoot();

    public static string PathTo(params string[] parts) => Path.Combine([Root, .. parts]);

    /// Every file under <paramref name="folder"/>, skipping build output and installed packages.
    public static IEnumerable<string> FilesUnder(string folder, params string[] extensions) =>
        Directory.Exists(PathTo(folder))
            ? Directory.EnumerateFiles(PathTo(folder), "*.*", SearchOption.AllDirectories)
                .Where(path => !IsGenerated(path))
                .Where(path => extensions.Length == 0 || extensions.Any(path.EndsWith))
            : [];

    private static bool IsGenerated(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment is "bin" or "obj" or "node_modules" or ".git" or "artifacts");

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FootballFormation.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("FootballFormation.slnx not found above the test output");
    }
}
