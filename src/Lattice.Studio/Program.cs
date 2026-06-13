using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lattice.Studio;
using Photino.NET;

// Lattice.Studio — the visual content editor (plan/08). Serves the API over the
// real content pipeline plus the built SPA, in a native desktop window.
//
//   dotnet run --project src/Lattice.Studio -- [--content <dir>] [--port N] [--no-window] [--browser]

var contentDir = ResolveContentDir(GetOption(args, "--content"));
if (!Directory.Exists(contentDir))
{
    Console.Error.WriteLine($"error: content directory not found: {Path.GetFullPath(contentDir)}");
    return 2;
}

var port = int.TryParse(GetOption(args, "--port"), out var p) ? p : 5210;
var useWindow = !args.Contains("--no-window");
var useBrowser = args.Contains("--browser");

var service = new StudioContentService(contentDir);
var live = new LiveSession(service.ContentDir, service.Context.Types);

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
builder.Services.AddSingleton(service);
builder.Services.AddSingleton(live);

var app = builder.Build();
app.Lifetime.ApplicationStopping.Register(live.Dispose);

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

// The live, engine-equivalent content session: what a running host would see.
api.MapGet("/live", (LiveSession l) => Results.Json(l.Status()));

// All raw defs of a kind (for whole-domain views like the GOAP action graph).
api.MapGet("/content/kind/{kind}", (string kind, StudioContentService s) =>
    Results.Text(s.DefsOfKind(kind).ToJsonString(), "application/json"));

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

// Start the web host without blocking, then own the main thread with the
// native window (or just wait, headless).
app.StartAsync().GetAwaiter().GetResult();
Console.WriteLine($"Lattice.Studio → {url}  (content: {service.ContentDir})");

if (useBrowser)
{
    TryOpenBrowser(url);
    app.WaitForShutdown();
}
else if (useWindow)
{
    try
    {
        new PhotinoWindow()
            .SetTitle("Lattice Studio")
            .SetUseOsDefaultSize(false)
            .SetSize(1480, 920)
            .Center()
            .Load(new Uri(url))
            .WaitForClose();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"(native window unavailable: {ex.Message}) — open {url} in a browser.");
        app.WaitForShutdown();
    }
}
else
{
    app.WaitForShutdown(); // --no-window: serve only (automation/headless)
}

app.StopAsync().GetAwaiter().GetResult();
return 0;

static string? GetOption(string[] args, string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

// Default content dir: walk up from the working directory to find a `content/`
// folder, so running from anywhere in the repo (or via `dotnet run`) just works.
static string ResolveContentDir(string? requested)
{
    if (!string.IsNullOrWhiteSpace(requested))
    {
        return requested;
    }

    for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
    {
        var candidate = Path.Combine(dir.FullName, "content");
        if (Directory.Exists(candidate))
        {
            return candidate;
        }
    }

    return "content";
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
