namespace RedfurSync.Tests;

internal sealed class TemporaryDirectory : IDisposable
{
    private bool _disposed;

    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"RedfurSync.Tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string CreateDirectory(params string[] segments)
    {
        var path = Resolve(segments);
        Directory.CreateDirectory(path);
        return path;
    }

    public string WriteFile(string relativePath, string contents)
    {
        var path = Resolve(relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return path;
    }

    public string WriteFile(string relativePath, ReadOnlySpan<byte> contents)
    {
        var path = Resolve(relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, contents.ToArray());
        return path;
    }

    private string Resolve(params string[] segments)
    {
        if (segments.Length == 0 || segments.Any(System.IO.Path.IsPathRooted))
            throw new ArgumentException("Fixture paths must be relative.", nameof(segments));

        var candidate = System.IO.Path.GetFullPath(System.IO.Path.Combine([Path, .. segments]));
        var root = System.IO.Path.GetFullPath(Path) + System.IO.Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(root, StringComparison.Ordinal))
            throw new ArgumentException("Fixture path escapes the temporary directory.", nameof(segments));
        return candidate;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Directory.Delete(Path, recursive: true);
    }
}