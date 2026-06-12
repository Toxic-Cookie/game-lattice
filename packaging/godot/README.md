# Game Lattice (Godot addon)

Engine-agnostic, JSON-driven RPG framework shipped as precompiled `netstandard2.1`
assemblies for **Godot 4 (.NET edition)**. This is a C# library — there are no
GDScript bindings and no editor plugin, so nothing appears under
Project Settings → Plugins; the addon simply puts the assemblies in your project.

Full documentation: <https://github.com/Toxic-Cookie/game-lattice>

## Prefer NuGet if you can

For most Godot .NET projects the cleaner install is NuGet, which handles updates
and transitive dependencies for you:

```
dotnet add package GameLattice
```

Use this addon when you want vendored, offline DLLs checked into your project, or
when installing from the Godot Asset Store.

## Using the addon

1. Copy `addons/game_lattice/` into your project (the Asset Store import does
   this for you).
2. Reference the assemblies from your project's `.csproj`:

   ```xml
   <Import Project="addons/game_lattice/GameLattice.props" />
   ```

   `GameLattice.props` adds references to every DLL in `addons/game_lattice/bin/`.

`bin/` contains the five framework assemblies (`Lattice.Core`, `Lattice.Rpg`,
`Lattice.Narrative`, `Lattice.Ai`, `Lattice.World`) plus their full dependency
closure (NCalc, YarnSpinner, System.Text.Json, ...). If your project already
references one of those packages via NuGet, drop the duplicate from `bin/` and
remove its line from `GameLattice.props`.
