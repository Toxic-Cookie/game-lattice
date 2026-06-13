import { useCallback, useEffect, useMemo, useState } from "react";
import { api, type Catalog, type ContentIndex, type JsonSchema, type ValidationResult } from "./api.ts";
import { Editor } from "./Editor.tsx";
import { NewDefDialog } from "./NewDefDialog.tsx";
import type { RefOption } from "./RefPicker.tsx";

export function App() {
  const [index, setIndex] = useState<ContentIndex | null>(null);
  const [validation, setValidation] = useState<ValidationResult | null>(null);
  const [schemas, setSchemas] = useState<Record<string, JsonSchema>>({});
  const [catalog, setCatalog] = useState<Catalog | null>(null);
  const [error, setError] = useState<string | null>(null);

  const [query, setQuery] = useState("");
  const [kind, setKind] = useState<string | null>(null);
  const [file, setFile] = useState<string | null>(null);
  const [selected, setSelected] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);

  const refresh = useCallback(() => {
    api.content().then(setIndex).catch((e) => setError(String(e)));
    api.validate().then(setValidation).catch(() => {});
  }, []);

  useEffect(() => {
    refresh();
    api.schemas().then((b) => setSchemas(b.kinds)).catch(() => {});
    api.catalog().then(setCatalog).catch(() => {});
    const hash = decodeURIComponent(location.hash.replace(/^#/, ""));
    if (hash) setSelected(hash);
    if (new URLSearchParams(location.search).has("new")) setCreating(true);
  }, [refresh]);

  // Ref pickers resolve ids from the live index; union builders from the catalog.
  const optionsByKind = useMemo(() => {
    const m: Record<string, RefOption[]> = {};
    for (const d of index?.defs ?? []) (m[d.kind] ??= []).push({ id: d.id, description: d.description, kind: d.kind });
    return m;
  }, [index]);

  const unions = useMemo(
    () => ({ effect: catalog?.effects ?? [], condition: catalog?.conditions ?? [], task: catalog?.tasks ?? [] }),
    [catalog],
  );

  // Keep the URL hash in sync so a def is deep-linkable/shareable.
  const select = useCallback((id: string | null) => {
    setSelected(id);
    location.hash = id ? `#${encodeURIComponent(id)}` : "";
  }, []);

  const kindCounts = useMemo(() => {
    const counts = new Map<string, number>();
    for (const d of index?.defs ?? []) counts.set(d.kind, (counts.get(d.kind) ?? 0) + 1);
    return [...counts.entries()].sort((a, b) => a[0].localeCompare(b[0]));
  }, [index]);

  const rows = useMemo(() => {
    const q = query.trim().toLowerCase();
    return (index?.defs ?? []).filter((d) => {
      if (kind && d.kind !== kind) return false;
      if (file && d.sourceFile !== file) return false;
      if (!q) return true;
      return (
        d.id.toLowerCase().includes(q) ||
        (d.description ?? "").toLowerCase().includes(q) ||
        d.kind.toLowerCase().includes(q)
      );
    });
  }, [index, query, kind, file]);

  if (error) return <div className="fatal">Failed to load: {error}</div>;
  if (!index) return <div className="loading">Loading content…</div>;

  return (
    <div className="app">
      <header className="topbar">
        <div className="brand">
          Lattice <span>Studio</span>
        </div>
        <div className="stats">
          <span className="pill">{index.count} defs</span>
          <span className="pill">{kindCounts.length} kinds</span>
          <span className="pill">{index.files.length} files</span>
          {validation && (
            <span className={`pill ${validation.ok ? "ok" : "bad"}`}>
              {validation.ok
                ? "✓ valid"
                : `✕ ${validation.errors.length} error${validation.errors.length === 1 ? "" : "s"}`}
            </span>
          )}
        </div>
      </header>

      <div className="body">
        <aside className="sidebar">
          <button className={`kind ${kind === null ? "active" : ""}`} onClick={() => setKind(null)}>
            <span>All kinds</span>
            <span className="count">{index.count}</span>
          </button>
          {kindCounts.map(([k, n]) => (
            <button key={k} className={`kind ${kind === k ? "active" : ""}`} onClick={() => setKind(k)}>
              <span>{k}</span>
              <span className="count">{n}</span>
            </button>
          ))}
        </aside>

        <main className="content">
          <div className="toolbar">
            <input
              className="search"
              placeholder="Search id, description, kind…"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
            />
            <select className="filefilter" value={file ?? ""} onChange={(e) => setFile(e.target.value || null)}>
              <option value="">All files</option>
              {index.files.map((f) => (
                <option key={f} value={f}>
                  {f}
                </option>
              ))}
            </select>
            <span className="resultcount">
              {rows.length} of {index.count}
            </span>
            <button className="newdef" onClick={() => setCreating(true)}>
              + New
            </button>
          </div>

          <div className="tablewrap">
            <table>
              <thead>
                <tr>
                  <th className="c-kind">Kind</th>
                  <th className="c-id">ID</th>
                  <th className="c-desc">Description</th>
                  <th className="c-inh">Inherits</th>
                  <th className="c-file">File</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((d) => (
                  <tr
                    key={d.id}
                    className={`row ${selected === d.id ? "selected" : ""}`}
                    onClick={() => select(d.id)}
                  >
                    <td className="c-kind">
                      <span className="kindtag">{d.kind}</span>
                    </td>
                    <td className="c-id">
                      <code>{d.id}</code>
                    </td>
                    <td className="c-desc">{d.description}</td>
                    <td className="c-inh">{d.inherits && <code className="muted">{d.inherits}</code>}</td>
                    <td className="c-file">
                      <span className="muted">{d.sourceFile}</span>
                    </td>
                  </tr>
                ))}
                {rows.length === 0 && (
                  <tr>
                    <td colSpan={5} className="empty">
                      No defs match the current filters.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        </main>

        {selected && (
          <Editor
            id={selected}
            schemas={schemas}
            optionsByKind={optionsByKind}
            unions={unions}
            onClose={() => select(null)}
            onSaved={refresh}
            onGoTo={select}
            autoGraph={new URLSearchParams(location.search).has("graph")}
          />
        )}
      </div>

      {creating && (
        <NewDefDialog
          schemas={schemas}
          index={index}
          onClose={() => setCreating(false)}
          onCreated={(id) => {
            setCreating(false);
            refresh();
            select(id);
          }}
        />
      )}
    </div>
  );
}
