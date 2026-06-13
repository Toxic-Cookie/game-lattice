using Lattice.Ai;
using Lattice.Core.Content;
using Lattice.Core.Formulas;
using Lattice.Core.Hosting.Standalone;
using Lattice.Core.Simulation;
using Lattice.Narrative;
using Lattice.Rpg;
using Lattice.Tooling;
using Lattice.World;
using Yarn.Compiler;

// M1 scope: `validate` runs the full content pipeline (parse -> def load ->
// link pass -> formula pre-flight); `schemas` emits per-def-kind JSON schema
// groundwork. The rule registry grows per milestone (plan/06 §3).

if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
{
    Console.WriteLine("""
        lattice — Game Lattice content tooling

        Usage:
          lattice validate <contentDir>     full content validation (parse, refs, formulas)
          lattice manifest <contentDir> -o <file>   emit the LLM/modder content manifest (--json for structured)
          lattice schemas -o <outputDir>    emit .schema.json per def kind
          lattice --version                 print version
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

    var context = ToolingContext.Create();
    using var source = new DirectoryContentSource(args[1], watch: false);
    var registry = new DefRegistry();
    var loader = new ContentLoader(context.Types);
    var report = loader.LoadAll(source, registry);

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

    if (args.Contains("--json"))
    {
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
        {
            ok = report.Ok,
            defs = report.DefsLoaded,
            errors = report.Errors,
            warnings = report.Warnings,
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return report.Ok ? 0 : 1;
    }

    foreach (var warning in report.Warnings)
    {
        Console.WriteLine($"warning: {warning}");
    }

    foreach (var error in report.Errors)
    {
        Console.Error.WriteLine($"error: {error}");
    }

    Console.WriteLine($"validate: {report.DefsLoaded} def(s) in {source.EnumerateFiles().Count()} file(s), " +
                      $"{report.Errors.Count} error(s), {report.Warnings.Count} warning(s).");
    return report.Ok ? 0 : 1;
}

if (args[0] == "manifest")
{
    if (args.Length < 2 || !Directory.Exists(args[1]))
    {
        Console.Error.WriteLine("error: manifest requires an existing content directory argument");
        return 2;
    }

    var context = ToolingContext.Create();
    using var source = new DirectoryContentSource(args[1], watch: false);
    var registry = new DefRegistry();
    var report = new ContentLoader(context.Types).LoadAll(source, registry);
    if (!report.Ok)
    {
        foreach (var error in report.Errors)
        {
            Console.Error.WriteLine($"error: {error}");
        }

        return 1;
    }

    var json = args.Contains("--json");
    var output = json
        ? ManifestGenerator.GenerateJson(registry, context.Types, context.Effects, context.Conditions, context.Tasks)
        : ManifestGenerator.GenerateMarkdown(registry, context.Types, context.Effects, context.Conditions, context.Tasks);

    var outIndex = Array.IndexOf(args, "-o");
    if (outIndex >= 0 && outIndex + 1 < args.Length)
    {
        var outPath = args[outIndex + 1];
        if (Path.GetDirectoryName(outPath) is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(outPath, output);
        Console.WriteLine($"manifest: wrote {outPath} ({registry.Count} defs).");
    }
    else
    {
        Console.WriteLine(output);
    }

    return 0;
}

if (args[0] == "schemas")
{
    var outIndex = Array.IndexOf(args, "-o");
    if (outIndex < 0 || outIndex + 1 >= args.Length)
    {
        Console.Error.WriteLine("error: schemas requires -o <outputDir>");
        return 2;
    }

    var outputDir = args[outIndex + 1];
    Directory.CreateDirectory(outputDir);

    var context = ToolingContext.Create();
    SchemaGenerator.UnionVocabularies = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["effect"] = context.Effects.All.Select(e => e.Type).ToArray(),
        ["condition"] = context.Conditions.All.Select(c => c.Type).ToArray(),
        ["task"] = context.Tasks.All.Select(t => t.Type).ToArray(),
    };

    var count = 0;
    var kinds = context.Types.All.ToList();
    foreach (var (typeName, clrType) in kinds)
    {
        var schema = SchemaGenerator.GenerateSchemaJson(typeName, clrType);
        var path = Path.Combine(outputDir, $"{typeName}.schema.json");
        File.WriteAllText(path, schema);
        count++;
    }

    File.WriteAllText(
        Path.Combine(outputDir, "lattice.schema.json"),
        SchemaGenerator.GenerateCombinedSchemaJson(kinds.Select(k => k.Key)));

    Console.WriteLine($"schemas: {count} def kind(s) + lattice.schema.json -> {outputDir}");
    return 0;
}

Console.Error.WriteLine($"error: unknown command '{args[0]}' — try 'lattice --help'");
return 2;
