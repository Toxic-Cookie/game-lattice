import { useEffect, useMemo, useState } from "react";
import { api, type ContentIndex, type ValidationResult } from "./api.ts";

export function App() {
  const [index, setIndex] = useState<ContentIndex | null>(null);
  const [validation, setValidation] = useState<ValidationResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  const [query, setQuery] = useState("");
  const [kind, setKind] = useState<string | null>(null);
  const [file, setFile] = useState<string | null>(null);

  useEffect(() => {
    api.content().then(setIndex).catch((e) => setError(String(e)));
    api.validate().then(setValidation).catch(() => {});
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
                  <tr key={d.id}>
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
      </div>
    </div>
  );
}
