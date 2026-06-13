# Plan 08 — Content Editor (M8): Lattice.Studio

> **Status:** Draft v1 (2026-06-12)
> **Inputs:** the shipped tooling (`06-llm-modding.md` deliverables — schema generator, manifest exporter, validation CLI), the content corpus (108 defs across 33 kinds in `content/`).
> Implements the **"UI binding"** thread left open in `06-llm-modding.md` (M6) and the concept doc's human-authoring goal.

Depends on: M1 (content pipeline), M6 (schemas + manifest + validation tooling), everything that registers def kinds (M2–M5).

---

## 1. Why

Content today is authored by hand-editing JSON or delegating to an LLM. Neither scales to *hundreds* of
defs: hand-editing is error-prone with no live reference/validation feedback, and LLM authoring is opaque
and unverifiable by non-engineers. **Lattice.Studio** is a visual editor that makes managing the corpus a
breeze for humans, while staying truthful to the data model — it never becomes a second source of truth.

The framework is unusually editor-ready because the M6 tooling already emits, from the C# def types as
the single source of truth, everything an editor needs:

- **JSON Schemas** (`SchemaGenerator`) carrying editor-affordance metadata — `x-lattice-ref:"<kind>"` on
  every cross-def ID field, `x-lattice-union:"effect|condition|task"` on primitive payloads (with an
  `enum` of valid type names), the `type` `const` discriminator, and `inherits`.
- **A live catalog** (`ManifestGenerator.GenerateJson`) — every def (id/kind/description/inherits) plus
  the documented arg signatures for every effect/condition/task/steering primitive.
- **Authoritative validation** (`validate` pipeline) — the runtime's real loader, link pass, formula
  pre-flight, and Yarn compile-check.

So the editor does **not** reimplement game semantics. It references the real assemblies in-process and
wraps them in an HTTP API — the same axiom as the rest of the framework: tooling derives from the def
models and can never drift.

---

## 2. Design Axioms (editor-specific)

1. **The JSON files remain the source of truth.** The editor reads and writes `content/**/*.json` in
   place; it composes with hand-editing, LLM authoring, and git diffs/PRs rather than replacing them.
2. **Zero semantic duplication.** Schemas, catalog, and validation all come from the existing tooling
   in-process. The editor adds *presentation*, never *meaning*.
3. **Engine-agnostic.** A standalone .NET web app, not an in-Unity/in-Godot tool — building inside one
   engine would pick a side and get built twice, against the framework's whole thesis.
4. **Clean diffs.** Edits are byte-stable for untouched defs so PRs show only real changes.

---

## 3. Architecture

New project **`src/Lattice.Studio/`** (ASP.NET Core minimal API, `net10.0`) referencing the same
libraries `Lattice.Tooling` does. A **React + TypeScript SPA** under `src/Lattice.Studio/web/` is built
(Vite) and served as static assets. A thin **Photino.NET** shell opens a native window onto the local
server (plain-browser fallback). Single process, no Electron.

```
Photino window ─► localhost ─► ASP.NET (in-process: SchemaGenerator / ManifestGenerator / validate / file IO)
                                   │
   React SPA (forms + React Flow) ─┘   reads/writes  content/**/*.json  ─►  engine HotReloadManager (live preview)
```

### 3.1 Shared authoring context (done)

The three CLI commands previously assembled the effect/condition/task + def-type registries inline,
three times over. Extracted to **`ToolingContext.Create()`** (`src/Lattice.Tooling/ToolingContext.cs`) so
the CLI and Studio share one definition of "the full Lattice authoring context." Verified
byte-identical `schemas`/`manifest`/`validate` output before and after; full test suite green.

### 3.2 Backend API (each endpoint a thin wrapper over existing tooling)

- `GET  /api/schemas` — per-kind schemas + `lattice.schema.json` (`SchemaGenerator`, with the
  `UnionVocabularies` wired from `ToolingContext`).
- `GET  /api/catalog` — `ManifestGenerator.GenerateJson(...)`; powers ref pickers and union builders.
- `POST /api/validate` — the real validate pipeline; returns `{ ok, errors[], warnings[] }`.
- `GET  /api/content` — file tree + def index (id, kind, sourceFile) from `Def.SourceFile`.
- `GET  /api/content/def/{id}` — the raw `JsonObject` for one def (DOM-level, not deserialized).
- `PUT  /api/content/def/{id}` — apply an edited def, re-validate, write its `SourceFile`.
- `POST /api/content/def` — create a new def (placement per §3.4).
- `--content <dir>` argument, default `content/`, mirroring the CLI.

### 3.3 Round-trip fidelity

Edit at the **JSON-DOM level** (`JsonNode`/`JsonObject`), mutating only changed fields, writing with
`ContentLoader.JsonOptions` (canonical camelCase + indentation). Preserve each file's container form —
bare array vs single object vs `{"$schema","defs":[…]}` wrapper (all three already handled in
`ContentLoader.ParseRawDefs`). **Known limitation:** System.Text.Json cannot emit comments, so saving a
file containing `//` comments drops them. Current content is comment-free; we keep it so. A
JSONC-preserving writer is out of scope for v1.

### 3.4 File placement

*Edits* are already solved: `Def.SourceFile` records each def's origin file; the writer re-serializes
that file with the one def replaced. *New* defs use an overridable **kind→file routing map** seeded by
where existing defs of that kind already live (majority-file wins); the create dialog lets the author
pick a different existing file. Overrides persist in `studio.config.json` beside the content dir.

### 3.5 Frontend

- **Schema-driven form renderer** with custom widgets keyed on the `x-lattice-*` keywords that
  off-the-shelf generators ignore: `x-lattice-ref` → searchable ID combobox from the catalog (+go-to-def);
  `x-lattice-union` → primitive picker revealing the chosen primitive's documented arg fields;
  `inherits` → blueprint-aware inherited/overridden field display.
- **Master browser** — one virtualized table across all kinds: search, kind/tag facets, blueprint tree,
  validation badges. This is what makes "hundreds" a breeze.
- **Node-graph canvas (React Flow)** for graph-shaped kinds, via a per-kind adapter mapping def JSON ⇄
  `{nodes, edges}`: dialogue trees, BT/HTN hierarchies, FSM states/transitions, GOAP goal/action links.
- **Live validation** — debounced `POST /api/validate`, errors/warnings surfaced inline.

---

## 4. Milestones

- **M8.0 — Foundations** *(low-risk, done)*
  - [x] Commit this design doc.
  - [x] `.vscode/settings.json` maps `content/**/*.json` to the generated `lattice.schema.json` for
        immediate autocomplete/validation while Studio is built (free stopgap; does not unlock ref
        pickers — VS Code ignores the `x-lattice-*` keywords).
  - [x] Extract `ToolingContext`; refactor the three CLI commands onto it (byte-identical output).
- **M8.1 — Backend + read-only browser [core]**
  - [ ] Scaffold `Lattice.Studio` (API + Vite SPA + Photino shell); register in `game-lattice.slnx`.
  - [ ] `/api/schemas`, `/api/catalog`, `/api/content`; master browser table (read-only).
  - Acceptance: `/api/validate` output matches `lattice validate content/ --json` on identical input.
- **M8.2 — Form editing, one kind end-to-end [core]**
  - [ ] Schema-driven form for a data kind (e.g. `item`): load → edit → live validate → diff → save.
  - Acceptance: saving an unchanged def yields an empty `git diff`; a one-field change diffs one line.
- **M8.3 — Full data-kind editing [core]**
  - [ ] `x-lattice-ref` pickers, `x-lattice-union` builders, inherits-aware forms across all data kinds.
  - [ ] Create / clone-from-blueprint + file-placement routing.
  - Acceptance: create a new item via UI → `lattice validate content/` passes; a dangling ref surfaces
        the same error inline as the CLI emits.
- **M8.4 — Node-graph canvas [core]**
  - [ ] React Flow + adapter, starting with `dialogue`, then `btree`/`fsmbrain`.
  - [ ] GOAP/HTN graph views [stretch].
- **M8.5 — Engine hot-reload preview [stretch]**
  - [ ] Verify the Godot/Unity samples pick up `Content.Reloaded` on Studio saves; add an in-Studio
        "validation mirrors the running engine" affordance.

---

## 5. Verification

- **Refactor safety (M8.0, done):** `schemas`/`manifest`/`validate` output byte-identical before/after;
  `dotnet test` green (278 tests).
- **Backend (M8.1+):** `dotnet run --project src/Lattice.Studio -- --content content/`; assert
  `/api/validate` ≡ `lattice validate content/ --json`.
- **Round-trip (M8.2):** unchanged save ⇒ empty `git diff`; single-field change ⇒ single-line diff.
- **End-to-end (M8.3+):** UI-created def passes the CLI validator; injected dangling ref reproduces the
  CLI's exact error inline.
- **Live preview (M8.5):** edit a def in Studio with a sample engine running; observe the change via
  `Content.Reloaded` without restart.

---

## 6. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| **Comment loss on save** | Accepted for v1; content kept comment-free; JSONC-preserving writer is later scope |
| **JS toolchain in a C# repo** | SPA builds only in the Studio project's publish step, not in core library CI lanes |
| **Scope — this is a product** | Milestones front-load value (M8.0–M8.1 useful within days) and pause cleanly between phases |
| **Forms are wrong for behavioral kinds** | Node-graph canvas (M8.4) is first-class scope, not an afterthought |
| **Editor drifting from the model** | No semantic duplication: schemas/catalog/validation all in-process from `ToolingContext` |

---

## 7. Open Questions

- Project name (`Lattice.Studio` proposed) and whether to later fold launch into the CLI as
  `lattice studio`.
- First node-graph kind confirmed as `dialogue` (self-contained, highest visual payoff).
- None block starting M8.1.
