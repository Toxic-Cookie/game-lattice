using System.Text.Json;
using Lattice.Core.Hosting.Standalone;

// M0 scope: `lattice validate <dir>` checks every JSON content file parses.
// The full rule registry (schemas, ID references, formulas, ...) accretes
// per-milestone; see plan/06-llm-modding.md §3.

if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
{
    Console.WriteLine("""
        lattice — Game Lattice content tooling

        Usage:
          lattice validate <contentDir>   validate content JSON (M0: well-formedness)
          lattice --version               print version
        """);
    return args.Length == 0 ? 1 : 0;
}

if (args[0] is "--version" or "-v")
{
    var version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    Console.WriteLine($"lattice {version}");
    return 0;
}

if (args[0] == "validate")
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("error: validate requires a content directory argument");
        return 2;
    }

    if (!Directory.Exists(args[1]))
    {
        Console.Error.WriteLine($"error: directory not found: {args[1]}");
        return 2;
    }

    using var source = new DirectoryContentSource(args[1], watch: false);
    var errorCount = 0;
    var fileCount = 0;

    foreach (var file in source.EnumerateFiles())
    {
        fileCount++;
        try
        {
            using var _ = JsonDocument.Parse(
                source.ReadAllText(file),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            errorCount++;
            Console.Error.WriteLine($"error: {file.RelativePath}: {ex.Message}");
        }
    }

    Console.WriteLine($"validate: {fileCount} file(s) checked, {errorCount} error(s).");
    return errorCount == 0 ? 0 : 1;
}

Console.Error.WriteLine($"error: unknown command '{args[0]}' — try 'lattice --help'");
return 2;
