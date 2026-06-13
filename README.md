# Game Lattice (Unity package)

Engine-agnostic, JSON-driven RPG framework shipped as precompiled `netstandard2.1`
managed plugins. Requires **Unity 2021.2+** (the first version with .NET Standard 2.1
support).

Full documentation: <https://github.com/Toxic-Cookie/game-lattice>

New to the framework? Start with the
[interactive Unity playground](https://github.com/Toxic-Cookie/game-lattice-unity-example) —
nine hands-on lessons over a live session.

## Install

- **OpenUPM**: `openupm add com.gamelattice.lattice`
- **Git URL** (Package Manager → Add package from git URL):
  `https://github.com/Toxic-Cookie/game-lattice.git#upm`
- **Tarball**: download `com.gamelattice.lattice-<version>.tgz` from a
  [GitHub release](https://github.com/Toxic-Cookie/game-lattice/releases) and use
  Package Manager → Add package from tarball.

## What's inside

`Runtime/` contains the five framework assemblies (`Lattice.Core`, `Lattice.Rpg`,
`Lattice.Narrative`, `Lattice.Ai`, `Lattice.World`) plus their full dependency
closure, because Unity has no NuGet restore: NCalc, Parlot, YarnSpinner (+ Antlr4,
Google.Protobuf, CsvHelper), System.Text.Json and its support assemblies, and a few
`Microsoft.Extensions.*` abstractions. Assemblies Unity itself references for every
compilation (`Microsoft.CSharp`) are deliberately *not* bundled — a copy would
collide with Unity's own (CS1703); Unity's satisfies the closure at runtime.

## Known conflicts

If your project already contains any of the bundled assemblies — most commonly
**System.Text.Json**, **Google.Protobuf** (gRPC/Firebase), or **YarnSpinner**
(Yarn Spinner for Unity) — Unity will report
`Multiple precompiled assemblies with the same name`. Delete the duplicate copy
from this package's `Runtime/` folder (embed the package first, or use the other
copy project-wide). This is an inherent limitation of plugin distribution in Unity,
not something the package can detect for you.
