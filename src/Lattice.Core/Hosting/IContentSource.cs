namespace Lattice.Core.Hosting;

/// <summary>A content file as seen by the framework, addressed by a normalized relative path.</summary>
/// <param name="AbsolutePath">Full path usable for direct IO by the source itself.</param>
/// <param name="RelativePath">Path relative to the content root, normalized to forward slashes.</param>
public readonly record struct ContentFile(string AbsolutePath, string RelativePath);

/// <summary>Kind of change reported by a content source watcher.</summary>
public enum ContentChangeKind
{
    Created,
    Modified,
    Deleted,
}

/// <summary>A single observed content change.</summary>
public readonly record struct ContentChange(ContentChangeKind Kind, ContentFile File);

/// <summary>
/// Where content JSON comes from and how the framework learns it changed.
/// The default implementation reads a directory tree; hosts may substitute
/// pack files, addressables, or embedded resources.
/// </summary>
/// <remarks>
/// <see cref="Changed"/> is raw and may fire in bursts on arbitrary threads;
/// debouncing/marshalling is the consumer's job (the hot-reload manager, M1).
/// </remarks>
public interface IContentSource : IDisposable
{
    /// <summary>Enumerate content files matching <paramref name="searchPattern"/>, recursively.</summary>
    IEnumerable<ContentFile> EnumerateFiles(string searchPattern = "*.json");

    /// <summary>Read the full text of a previously enumerated file.</summary>
    string ReadAllText(ContentFile file);

    /// <summary>Raised when a content file is created, modified, or deleted.</summary>
    event Action<ContentChange>? Changed;
}
