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
- **M8.1 — Backend + read-only browser [core]** *(done)*
  - [x] Scaffold `Lattice.Studio` (ASP.NET minimal API + Vite/React/TS SPA); registered in
        `game-lattice.slnx`. *(Photino native-window shell deferred — the browser-launch fallback the
        plan allows is in place; a real launch defaults the content dir relative to the working dir.)*
  - [x] `/api/schemas`, `/api/catalog`, `/api/content`, `/api/validate`; read-only master browser
        (kind facets + counts, search, file filter, validation badge) over all 108 defs.
  - [x] Shared `ContentValidation` extracted into `Lattice.Tooling` so CLI and Studio run one pipeline.
  - Acceptance met: `/api/validate` is byte-identical to `lattice validate content/ --json`; full
    solution builds clean; 278 tests green.
- **M8.2 — Form editing, one kind end-to-end [core]** *(done)*
  - [x] `GET/PUT /api/content/def/{id}` + `ContentDocument`, a format-preserving writer that splices only
        changed top-level value spans (scalars + inline primitive arrays), no-ops on deep-equal, and
        re-validates the whole tree on save. Untouched defs/fields stay byte-identical.
  - [x] Schema-driven form editor panel (scalars, booleans, numbers, string-array tokens; object/union
        fields read-only until M8.3), client-side change summary, save + validation display. Defs are
        deep-linkable via URL hash.
  - Acceptance met: all 108 defs round-trip as no-ops (empty `git diff`); single-field edits produce a
    one-line diff localized to that def (verified on `item_gold`, `item_iron_sword`, `entity_wolf`);
    278 tests green.
- **M8.3a — Editing widgets [core]** *(done)*
  - [x] `x-lattice-ref` picker (searchable combobox from the live index, multi-kind, go-to-def,
        dangling indicator) — single fields and ref arrays.
  - [x] `x-lattice-union` builder (primitive-type dropdown, arg fields parsed from the catalog
        signature with ref-picker integration, documented example, raw-JSON escape) — single payloads
        and union arrays. Plus a raw-JSON editor for other nested object/array fields.
  - [x] Inherits-aware display: parent picker + collapsible "inherited from parent" section.
  - [x] Writer upgraded: compact single-line rendering for all-primitive objects (tasks/effects),
        so union-array edits also produce minimal diffs; correct nested indentation.
  - Acceptance met: union edit (`schedule_patrol` Wait) → one-line diff; dangling ref save surfaces the
    CLI's exact `Dangling reference` error inline; all 108 defs still round-trip as no-ops; 278 green.
- **M8.3b — Authoring new defs [core]** *(done)*
  - [x] `POST /api/content/def` + `ContentDocument.AppendDef` (surgical append into a bare-array file;
        renderer fallback for other shapes) and `NewFile`. `CreateDef` routes by majority file per kind
        (from `Def.SourceFile`), guards duplicate id / unknown type, and re-validates.
  - [x] UI: toolbar **+ New** dialog (kind, id, file with client-computed majority suggestion +
        datalist of existing files) and a **clone** action in the editor header.
  - [x] Informative kind picker: each def type's XML `<summary>` is emitted into its schema
        `description` (via `GenerateDocumentationFile` + `XmlDocs`), and the New dialog is a searchable
        browser showing every kind's title, description ("what it is and does"), and existing count.
  - [x] Same XML-summary treatment extended to **field-level** docs (property `<summary>` → schema
        property `description`, shown as a hint under each editor field — 205/277 properties) and the
        **LLM manifest** (kind descriptions under each section, in markdown and `--json`).
  - Acceptance met: new item routed to `items.json`, appended with a clean diff (prior def gains a
    comma), `lattice validate` passes (109 defs); clone copies a full def under a new id; duplicate-id
    and unknown-type rejected; CLI validate still byte-identical; 278 tests green.
- **M8.4 — Node-graph canvas [core]**
  - **M8.4a — Read-only graphs (4 kinds)** *(done)*: React Flow canvas overlay (opened from a
    `⊞ graph` button in the editor for any graph kind). `graph.ts` holds a generic node card + a
    per-kind adapter registry, all laid out by BFS depth:
    - `dialogue` — nodes (speaker/line, start, option/effect counts, terminal); option→`next` edges
      labeled with option text, dashed/gold when gated; `next` continues.
    - `btree` — the `root` tree: composite/decorator/task/subtree/condition nodes (color-coded tags),
      `ConditionGate.when` shown, task arg summaries, numbered child edges.
    - `fsmbrain` — states (steering type) + transition edges labeled with the condition summary.
    - `htncompound` — compound → ordered methods (precondition + order) → cross-def subtask refs.
    Shared condition summarizer (e.g. `any(THREAT_KNOWN, CAN_SEE_ENEMY)`). Verified on `tree_guard`,
    `bt_patron`, `fsmbrain_rat`, `htn_forage`.
  - **M8.4b — Graph editing (dialogue/fsm)** *(done)*: `graph.ts` gains an `editable` ops registry
    (connect / reconnect / removeEdge / addNode / removeNode → new def JSON, scrubbing dangling refs).
    The canvas is stateful: drag a node's source dot to another node to add an option/transition,
    drag a link end to rewire, select + Delete to remove, `+ node` to add; a dirty-tracked **Save**
    PUTs and shows validation. The editor reloads its draft after a graph save (no stale overwrite).
    btree/htn stay read-only. Writer refined: a short array of compact objects stays inline
    (`[ { … } ]`), so editing a dialogue node no longer churns sibling `conditions`/`effects`.
    Verified: UI `+ node` + Save writes a minimal diff and validates; add-node+option round-trips
    minimally; 108 defs still no-op; multi-element arrays stay multiline; 278 tests green.
  - [ ] GOAP precondition/effect views [stretch].
- **M8.5 — Engine hot-reload preview [stretch]**
  - [ ] Verify the Godot/Unity samples pick up `Content.Reloaded` on Studio saves; add an in-Studio
        "validation mirrors the running engine" affordance.
- **M8.x — Polish [deferred]**
  - [ ] **Photino.NET native-window shell** (deferred from M8.1): replace the browser-launch fallback
        with a real desktop window; resolve the default `--content` path against the repo root, not cwd.
  - [ ] Virtualize the browser table once the corpus outgrows a plain render.

> **Separate enhancement (out of scope for this roadmap): doc-comment quality pass.** Now that the def
> types' XML `<summary>` comments are surfaced to *content authors* (editor field hints, kind browser,
> JSON schemas, manifest), they should read as end-user documentation, not developer/design notes.
> Two follow-ups, to be done as their own effort: (1) **document the remaining ~72/277 properties** that
> have no `<summary>`; (2) **rewrite the comments for an author audience** — strip the internal milestone/
> chapter/case-study references (`plan/0X §Y`, `chNN`, "F.E.A.R. rat problem", "HZD Part 4", etc.) that
> mean nothing to someone authoring content. Consider splitting design rationale into `<remarks>` (kept
> for devs) and a clean author-facing `<summary>` (surfaced by tooling).

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
