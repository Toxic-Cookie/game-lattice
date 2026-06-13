using System.Text.Json.Serialization;
using Lattice.Ai;
using Lattice.Core.Content;
using Lattice.Core.Formulas;
using Lattice.Core.Hosting.Standalone;
using Lattice.Core.Simulation;
using Lattice.Narrative;
using Lattice.Rpg;
using Lattice.World;
using Yarn.Compiler;

namespace Lattice.Tooling;

/// <summary>Structured outcome of a content validation run (the shape the <c>validate --json</c> command and Studio both serialize).</summary>
/// <param name="Ok">True when there are no errors.</param>
/// <param name="DefsLoaded">Count of defs successfully applied to the registry.</param>
/// <param name="FileCount">Count of content files seen by the loader.</param>
/// <param name="Errors">Blocking problems (dangling refs, bad formulas, parse failures, Yarn errors).</param>
/// <param name="Warnings">Non-blocking advisories.</param>
public sealed record ValidationResult(
    bool Ok,
    [property: JsonPropertyName("defs")] int DefsLoaded,
    [property: JsonPropertyName("files")] int FileCount,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

/// <summary>
/// The full content validation pipeline (parse -> def load -> link pass ->
/// formula pre-flight -> per-module validators -> Yarn compile-check), shared
/// by the <c>lattice validate</c> command and Lattice.Studio so the editor's
/// inline validation can never diverge from the CLI/CI verdict.
/// </summary>
public static class ContentValidation
{
    public static ValidationResult Run(ToolingContext context, string contentDir)
    {
        using var source = new DirectoryContentSource(contentDir, watch: false);
        var registry = new DefRegistry();
        var report = new ContentLoader(context.Types).LoadAll(source, registry);

        // formula pre-flight uses a throwaway RNG: TryParse never rolls dice
        var formulas = new NCalcFormulaEngine(new LatticeRandom(0));
        registry.Validate(report, formulas);
        new RpgContentValidator(context.Effects, context.Conditions).Validate(registry, report, formulas);
        new NarrativeContentValidator(context.Effects, context.Conditions).Validate(registry, report, formulas);
        new AiContentValidator(context.Conditions, context.Tasks, context.Effects).Validate(registry, report, formulas);
        new WorldContentValidator(context.Effects).Validate(registry, report, formulas);

        // Yarn scripts compile-check (same function library the runtime registers)
        var yarnFiles = source.EnumerateFiles("*.yarn").Select(f => f.AbsolutePath).ToArray();
        if (yarnFiles.Length > 0)
        {
            var yarnResult = Compiler.Compile(CompilationJob.CreateFromFiles(yarnFiles, NarrativeRuntime.CreateCompilationLibrary()));
            foreach (var diagnostic in yarnResult.Diagnostics)
            {
                var message = $"{Path.GetFileName(diagnostic.FileName)}:{diagnostic.Range.Start.Line + 1}: {diagnostic.Message}";
                if (diagnostic.Severity == Diagnostic.DiagnosticSeverity.Error)
                {
                    report.Errors.Add(message);
                }
                else
                {
                    report.Warnings.Add(message);
                }
            }
        }

        return new ValidationResult(report.Ok, report.DefsLoaded, source.EnumerateFiles().Count(), report.Errors, report.Warnings);
    }
}
