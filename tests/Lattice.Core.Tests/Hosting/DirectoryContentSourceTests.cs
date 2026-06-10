using Lattice.Core.Hosting;
using Lattice.Core.Hosting.Standalone;

namespace Lattice.Core.Tests.Hosting;

public sealed class DirectoryContentSourceTests : IDisposable
{
    private readonly string _root;

    public DirectoryContentSourceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "lattice-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "items"));
        File.WriteAllText(Path.Combine(_root, "items", "sword.json"), """{ "id": "item_sword" }""");
        File.WriteAllText(Path.Combine(_root, "world.json"), """{ "id": "world" }""");
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "not content");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // temp dir cleanup is best-effort
        }
    }

    [Fact]
    public void EnumerateFiles_ReturnsJsonWithNormalizedRelativePaths()
    {
        using var source = new DirectoryContentSource(_root, watch: false);

        var relative = source.EnumerateFiles().Select(f => f.RelativePath).OrderBy(p => p).ToList();

        Assert.Equal(["items/sword.json", "world.json"], relative);
    }

    [Fact]
    public void ReadAllText_ReturnsFileContents()
    {
        using var source = new DirectoryContentSource(_root, watch: false);
        var file = source.EnumerateFiles().Single(f => f.RelativePath == "world.json");

        Assert.Contains("\"id\": \"world\"", source.ReadAllText(file));
    }

    [Fact]
    public void MissingRoot_Throws()
    {
        Assert.Throws<DirectoryNotFoundException>(
            () => new DirectoryContentSource(Path.Combine(_root, "does-not-exist"), watch: false));
    }

    [Fact]
    public async Task Watcher_ReportsModifiedFile()
    {
        using var source = new DirectoryContentSource(_root, watch: true);
        var seen = new TaskCompletionSource<ContentChange>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.Changed += change =>
        {
            if (change.File.RelativePath == "world.json")
            {
                seen.TrySetResult(change);
            }
        };

        await File.WriteAllTextAsync(Path.Combine(_root, "world.json"), """{ "id": "world", "edited": true }""");

        var completed = await Task.WhenAny(seen.Task, Task.Delay(TimeSpan.FromSeconds(10)));

        Assert.Same(seen.Task, completed);
        var observed = await seen.Task;
        Assert.Equal("world.json", observed.File.RelativePath);
        Assert.True(observed.Kind is ContentChangeKind.Modified or ContentChangeKind.Created);
    }
}
