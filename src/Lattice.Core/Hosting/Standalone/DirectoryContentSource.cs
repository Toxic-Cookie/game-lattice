namespace Lattice.Core.Hosting.Standalone;

/// <summary>
/// <see cref="IContentSource"/> over a directory tree, with an optional
/// <see cref="FileSystemWatcher"/> feeding <see cref="Changed"/>. Events are
/// forwarded raw (watcher thread, possibly in bursts); the hot-reload manager
/// (M1) owns debouncing.
/// </summary>
public sealed class DirectoryContentSource : IContentSource
{
    private readonly string _root;
    private readonly FileSystemWatcher? _watcher;

    public DirectoryContentSource(string rootDirectory, bool watch = true)
    {
        _root = Path.GetFullPath(rootDirectory);
        if (!Directory.Exists(_root))
        {
            throw new DirectoryNotFoundException($"Content root not found: {_root}");
        }

        if (watch)
        {
            _watcher = new FileSystemWatcher(_root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            _watcher.Created += (_, e) => Raise(ContentChangeKind.Created, e.FullPath);
            _watcher.Changed += (_, e) => Raise(ContentChangeKind.Modified, e.FullPath);
            _watcher.Deleted += (_, e) => Raise(ContentChangeKind.Deleted, e.FullPath);
            _watcher.Renamed += (_, e) =>
            {
                Raise(ContentChangeKind.Deleted, e.OldFullPath);
                Raise(ContentChangeKind.Created, e.FullPath);
            };
            _watcher.EnableRaisingEvents = true;
        }
    }

    public event Action<ContentChange>? Changed;

    public IEnumerable<ContentFile> EnumerateFiles(string searchPattern = "*.json")
    {
        return Directory
            .EnumerateFiles(_root, searchPattern, SearchOption.AllDirectories)
            .Select(path => new ContentFile(path, ToRelative(path)));
    }

    public string ReadAllText(ContentFile file) => File.ReadAllText(file.AbsolutePath);

    public void Dispose() => _watcher?.Dispose();

    private void Raise(ContentChangeKind kind, string fullPath)
    {
        if (Directory.Exists(fullPath))
        {
            return; // directory-level event; only files are content
        }

        Changed?.Invoke(new ContentChange(kind, new ContentFile(fullPath, ToRelative(fullPath))));
    }

    private string ToRelative(string fullPath)
    {
        var relative = fullPath.StartsWith(_root, StringComparison.OrdinalIgnoreCase)
            ? fullPath.Substring(_root.Length)
            : fullPath;
        return relative
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Replace('\\', '/');
    }
}
