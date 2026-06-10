using Lattice.Core.Content;
using Lattice.Core.Formulas;
using Lattice.Core.Hosting.Standalone;
using Lattice.Core.Simulation;
using Lattice.Tooling;

// M1 scope: `validate` runs the full content pipeline (parse -> def load ->
// link pass -> formula pre-flight); `schemas` emits per-def-kind JSON schema
// groundwork. The rule registry grows per milestone (plan/06 §3).

if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
{
    Console.WriteLine("""
        lattice — Game Lattice content tooling

        Usage:
          lattice validate <contentDir>     full content validation (parse, refs, formulas)
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

    using var source = new DirectoryContentSource(args[1], watch: false);
    var registry = new DefRegistry();
    var loader = new ContentLoader(DefTypeRegistry.CreateDefault());
    var report = loader.LoadAll(source, registry);

    // formula pre-flight uses a throwaway RNG: TryParse never rolls dice
    registry.Validate(report, new NCalcFormulaEngine(new LatticeRandom(0)));

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

    var count = 0;
    foreach (var (typeName, clrType) in DefTypeRegistry.CreateDefault().All)
    {
        var schema = SchemaGenerator.GenerateSchemaJson(typeName, clrType);
        var path = Path.Combine(outputDir, $"{typeName}.schema.json");
        File.WriteAllText(path, schema);
        Console.WriteLine($"wrote {path}");
        count++;
    }

    Console.WriteLine($"schemas: {count} def kind(s).");
    return 0;
}

Console.Error.WriteLine($"error: unknown command '{args[0]}' — try 'lattice --help'");
return 2;
