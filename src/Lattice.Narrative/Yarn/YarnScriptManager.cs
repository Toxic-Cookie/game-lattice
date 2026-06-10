using Lattice.Core.Hosting;
using Yarn;
using Yarn.Compiler;

namespace Lattice.Narrative.Yarn;

/// <summary>
/// Compiles every <c>*.yarn</c> file in the content source into one program
/// and resolves line text. Mirrors the JSON hot-reload contract: a broken
/// compile keeps the previous program and logs (plan/01 §6 applied to Yarn).
/// </summary>
public sealed class YarnScriptManager
{
    private CompilationResult? _result;

    public Program? Program => _result?.Program;

    public IReadOnlyList<string> LastErrors { get; private set; } = [];

    /// <summary>Compile all .yarn files. Returns false (keeping the old program) on errors.</summary>
    public bool Compile(IContentSource source, Library functionLibrary, ILatticeLogger logger)
    {
        var files = source.EnumerateFiles("*.yarn")
            .Select(f => f.AbsolutePath)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        if (files.Length == 0)
        {
            _result = null;
            LastErrors = [];
            return true;
        }

        CompilationResult result;
        try
        {
            result = Compiler.Compile(CompilationJob.CreateFromFiles(files, functionLibrary));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            LastErrors = [$"Yarn compilation failed: {ex.Message}"];
            logger.Error(LastErrors[0]);
            return false;
        }

        var errors = result.Diagnostics
            .Where(d => d.Severity == Diagnostic.DiagnosticSeverity.Error)
            .Select(d => $"{Path.GetFileName(d.FileName)}:{d.Range.Start.Line + 1}: {d.Message}")
            .ToList();

        if (errors.Count > 0)
        {
            LastErrors = errors;
            foreach (var error in errors)
            {
                logger.Error($"Yarn: {error}");
            }

            return false;
        }

        _result = result;
        LastErrors = [];
        logger.Info($"Yarn: compiled {files.Length} file(s), {result.Program?.Nodes.Count ?? 0} node(s).");
        return true;
    }

    public bool NodeExists(string nodeName) => Program?.Nodes.ContainsKey(nodeName) == true;

    /// <summary>Resolve a line's display text: string-table lookup + {n} substitutions.</summary>
    public string GetText(Line line)
    {
        var text = _result?.GetStringForKey(line.ID) ?? line.ID;
        for (var i = 0; i < line.Substitutions.Length; i++)
        {
            text = text.Replace("{" + i + "}", line.Substitutions[i]);
        }

        return text;
    }
}
