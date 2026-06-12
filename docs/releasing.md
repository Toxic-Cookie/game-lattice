# Releasing

`.github/workflows/release.yml` cuts a release on **every push to `main`** (i.e. every
merged PR). No manual steps are part of the regular flow.

## What one release produces

| Artifact | Where it goes |
|---|---|
| `GameLattice`, `GameLattice.Core/.Rpg/.Narrative/.Ai/.World` nupkgs + snupkgs | GitHub Release; nuget.org when `NUGET_API_KEY` is set |
| `GameLattice.Tooling` (dotnet tool, command `lattice`) | GitHub Release; nuget.org when `NUGET_API_KEY` is set |
| `com.gamelattice.lattice-<v>.tgz` (Unity UPM tarball) | GitHub Release |
| `upm` branch + `upm/<v>` tag (flat UPM layout) | Git-URL installs; OpenUPM builds from these tags |
| `game-lattice-addon-<v>.zip` (Godot `addons/game_lattice`) | GitHub Release |
| `godot-addon` branch + `godot/<v>` tag | Godot Asset Library downloads repo archives of this branch |

The Unity and Godot artifacts bundle the full dependency closure (NCalc, YarnSpinner,
System.Text.Json, ...) collected by `packaging/bundle/Lattice.Bundle.csproj`, because
neither engine restores NuGet packages. Build them locally with
`pwsh scripts/Build-UnityPackage.ps1 -Version 1.2.3` /
`pwsh scripts/Build-GodotAddon.ps1 -Version 1.2.3`.

## Versioning

- First release: the `<Version>` in `Directory.Build.props` (0.1.0), as-is.
- After that: **patch bump** of the latest `v*` tag on every merge.
- PR labels steer the pipeline:
  - `release:minor` — bump minor instead (`0.1.4` → `0.2.0`)
  - `release:major` — bump major instead (`0.1.4` → `1.0.0`)
  - `release:skip` — merge without releasing (docs-only PRs, CI tweaks, ...)
- `<Version>` in `Directory.Build.props` is only a local-dev fallback; release builds
  pass `-p:Version=` and the file is not bumped by the pipeline.
- Re-running a release workflow is safe: if the computed tag already exists, it skips.

## One-time registry setup

Publishing steps skip with a workflow notice until their secrets exist, so the pipeline
works (GitHub Releases only) before any of this is done.

### nuget.org

1. Sign in at nuget.org → API keys → create a key scoped to *Push new packages and
   package versions* with glob `GameLattice*`.
2. Repo → Settings → Secrets and variables → Actions → new secret `NUGET_API_KEY`.

That's it; the next release publishes all seven packages. Keys expire after at most a
year — rotate when the publish step starts failing with 401/403.

### OpenUPM (Unity)

After the **first** release has pushed an `upm/<v>` tag:

1. Go to <https://openupm.com/packages/add>, enter this repo.
2. In the generated package metadata set `gitTagPrefix: "upm/"` so OpenUPM only builds
   the flat-layout tags (not `v*` repo tags).
3. Submit — it opens a PR against `openupm/openupm`; once merged, OpenUPM's pipelines
   automatically build every future `upm/<v>` tag. No secrets needed in this repo.

### Godot Asset Library

The least robust leg (the AssetLib is in maintenance mode pending the Godot Asset
Store, and every edit passes human moderation):

1. After the first release, manually submit the asset at
   <https://godotengine.org/asset-library/asset/edit> — point it at this repo with the
   **`godot-addon` branch commit** as the download commit (the AssetLib downloads repo
   archives, and that branch contains the bare `addons/game_lattice` layout).
2. Once approved, note the asset id from its URL and add three repo secrets:
   `GODOT_ASSETLIB_USERNAME`, `GODOT_ASSETLIB_PASSWORD`, `GODOT_ASSETLIB_ASSET_ID`.
3. Each release then submits an edit (new `version_string` + download commit) via the
   AssetLib API; expect a moderation delay before it appears publicly.

## Package identity

`Lattice` and `Lattice.Core` were already taken on nuget.org, so package IDs use the
`GameLattice.*` prefix while assemblies and namespaces remain `Lattice.*`
(`src/Directory.Build.props`). Unity package name: `com.gamelattice.lattice`.
