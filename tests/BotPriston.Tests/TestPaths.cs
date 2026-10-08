namespace BotPriston.Tests;

internal static class TestPaths
{
    /// <summary>Repository root (the folder containing the solution file).</summary>
    public static string RepoRoot { get; } = FindRepoRoot();

    /// <summary>Screenshots captured from the real game; vision tests run against these.</summary>
    public static string Samples => Path.Combine(RepoRoot, "samples");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BotPriston.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}

internal sealed class TempFile : IDisposable
{
    private TempFile(string path) => Path = path;

    public string Path { get; }

    public static TempFile WithText(string text)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"botpriston_{Guid.NewGuid():N}.json");
        File.WriteAllText(path, text);
        return new TempFile(path);
    }

    public void Dispose() => File.Delete(Path);
}

internal sealed class TempDir : IDisposable
{
    private TempDir(string path) => Path = path;

    public string Path { get; }

    public static TempDir Create()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"botpriston_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return new TempDir(path);
    }

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
