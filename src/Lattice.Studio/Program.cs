using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lattice.Studio;

// Lattice.Studio — the visual content editor's local host (plan/08, M8.1).
// Serves the read-only API over the real content pipeline plus the built SPA.
//
//   dotnet run --project src/Lattice.Studio -- --content content/ [--port N] [--no-open]

var contentDir = GetOption(args, "--content") ?? "content";
if (!Directory.Exists(contentDir))
{
    Console.Error.WriteLine($"error: content directory not found: {Path.GetFullPath(contentDir)}");
    return 2;
}

var port = int.TryParse(GetOption(args, "--port"), out var p) ? p : 5210;
var open = !args.Contains("--no-open");

var service = new StudioContentService(contentDir);

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
builder.Services.AddSingleton(service);

var app = builder.Build();

// Read-only API (M8.1): every endpoint a thin projection of the shared tooling.
var api = app.MapGroup("/api");
api.MapGet("/schemas", (StudioContentService s) => Results.Text(s.Schemas().ToJsonString(), "application/json"));
api.MapGet("/catalog", (StudioContentService s) => Results.Text(s.Catalog().ToJsonString(), "application/json"));
api.MapGet("/content", (StudioContentService s) => Results.Text(s.Serialize(s.Index()), "application/json"));
api.MapGet("/validate", (StudioContentService s) =>
{
    var r = s.Validate();
    return Results.Json(new { ok = r.Ok, defs = r.DefsLoaded, files = r.FileCount, errors = r.Errors, warnings = r.Warnings });
});

// One def's raw JSON (for the form editor) and a minimal-diff save back to its file (M8.2).
api.MapGet("/content/def/{id}", (string id, StudioContentService s) =>
{
    var payload = s.GetDef(id);
    return payload is null ? Results.NotFound() : Results.Text(s.Serialize(payload), "application/json");
});
api.MapPut("/content/def/{id}", async (string id, HttpRequest req, StudioContentService s) =>
{
    JsonObject? def;
    try
    {
        def = (await JsonNode.ParseAsync(req.Body))?.AsObject();
    }
    catch (JsonException ex)
    {
        return Results.BadRequest(new { error = $"invalid JSON body: {ex.Message}" });
    }

    if (def is null)
    {
        return Results.BadRequest(new { error = "expected a def object" });
    }

    var result = s.SaveDef(id, def);
    return result.Status switch
    {
        "not_found" => Results.NotFound(),
        "error" => Results.BadRequest(new { error = result.Error }),
        _ => Results.Text(s.Serialize(result), "application/json"),
    };
});
api.MapPost("/content/def", async (HttpRequest req, StudioContentService s) =>
{
    JsonObject? body;
    try
    {
        body = (await JsonNode.ParseAsync(req.Body))?.AsObject();
    }
    catch (JsonException ex)
    {
        return Results.BadRequest(new { error = $"invalid JSON body: {ex.Message}" });
    }

    if (body?["def"] is not JsonObject def)
    {
        return Results.BadRequest(new { error = "expected { def, file? }" });
    }

    var result = s.CreateDef(def, (body["file"] as JsonValue)?.GetValue<string>());
    return result.Status == "error"
        ? Results.BadRequest(new { error = result.Error })
        : Results.Text(s.Serialize(result), "application/json");
});

// Serve the built SPA from wwwroot when present (absent until `npm run build`).
var wwwroot = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
if (Directory.Exists(wwwroot))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.MapFallbackToFile("index.html");
}

var url = $"http://127.0.0.1:{port}/";
Console.WriteLine($"Lattice.Studio → {url}  (content: {service.ContentDir})");
if (open)
{
    app.Lifetime.ApplicationStarted.Register(() => TryOpenBrowser(url));
}

app.Run();
return 0;

static string? GetOption(string[] args, string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static void TryOpenBrowser(string url)
{
    try
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"(could not open a browser automatically: {ex.Message}) — open {url} manually.");
    }
}
