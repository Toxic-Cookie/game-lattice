import { useMemo, useState } from "react";
import { api, type ContentIndex, type JsonSchema } from "./api.ts";

interface Props {
  schemas: Record<string, JsonSchema>;
  index: ContentIndex;
  onClose: () => void;
  onCreated: (id: string) => void;
}

// "AgentProfileDef" -> "Agent Profile"; falls back to the kind id.
function humanize(title: string | undefined, kind: string): string {
  if (!title) return kind;
  return title.replace(/Def$/, "").replace(/([a-z0-9])([A-Z])/g, "$1 $2");
}

export function NewDefDialog({ schemas, index, onClose, onCreated }: Props) {
  const [filter, setFilter] = useState("");
  const [kind, setKind] = useState("");
  const [id, setId] = useState("");
  const [file, setFile] = useState("");
  const [touchedFile, setTouchedFile] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const counts = useMemo(() => {
    const m = new Map<string, number>();
    for (const d of index.defs) m.set(d.kind, (m.get(d.kind) ?? 0) + 1);
    return m;
  }, [index]);

  const kinds = useMemo(
    () =>
      Object.keys(schemas)
        .sort()
        .map((k) => ({
          kind: k,
          title: humanize(schemas[k].title, k),
          description: schemas[k].description ?? "",
          count: counts.get(k) ?? 0,
        })),
    [schemas, counts],
  );

  const filtered = useMemo(() => {
    const q = filter.trim().toLowerCase();
    if (!q) return kinds;
    return kinds.filter(
      (k) => k.kind.includes(q) || k.title.toLowerCase().includes(q) || k.description.toLowerCase().includes(q),
    );
  }, [kinds, filter]);

  const selected = kinds.find((k) => k.kind === kind);

  const suggestedFile = useMemo(() => {
    const c = new Map<string, number>();
    for (const d of index.defs) if (d.kind === kind && d.sourceFile) c.set(d.sourceFile, (c.get(d.sourceFile) ?? 0) + 1);
    let best = "";
    let max = 0;
    for (const [f, n] of c) if (n > max) ((max = n), (best = f));
    return best || (kind ? `${kind}.json` : "");
  }, [kind, index]);
  const effectiveFile = touchedFile && file ? file : suggestedFile;

  const create = async () => {
    setBusy(true);
    setError(null);
    try {
      await api.create({ id: id.trim(), type: kind }, effectiveFile);
      onCreated(id.trim());
    } catch (e) {
      setError(String(e instanceof Error ? e.message : e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal wide" onClick={(e) => e.stopPropagation()}>
        <header className="modal-head">
          <span>New def — pick a kind</span>
          <button className="close" onClick={onClose}>✕</button>
        </header>

        <div className="modal-body">
          <input
            className="kindfilter"
            placeholder="Filter kinds by name or description…"
            value={filter}
            autoFocus
            onChange={(e) => setFilter(e.target.value)}
          />

          <ul className="kindlist">
            {filtered.map((k) => (
              <li key={k.kind} className={`kindrow ${kind === k.kind ? "active" : ""}`} onClick={() => setKind(k.kind)}>
                <div className="kindrow-head">
                  <code>{k.kind}</code>
                  <span className="kindtitle">{k.title}</span>
                  <span className="kindcount">{k.count}</span>
                </div>
                {k.description && <div className="kinddesc">{k.description}</div>}
              </li>
            ))}
            {filtered.length === 0 && <li className="empty">No kinds match “{filter}”.</li>}
          </ul>

          {selected ? (
            <div className="kindfooter">
              <div className="field">
                <label>id</label>
                <input
                  value={id}
                  spellCheck={false}
                  placeholder={`${selected.kind}_…`}
                  onChange={(e) => setId(e.target.value)}
                />
              </div>
              <div className="field">
                <label>
                  file <span className="muted">· where {selected.kind} defs live</span>
                </label>
                <input
                  list="content-files"
                  value={effectiveFile}
                  spellCheck={false}
                  onChange={(e) => {
                    setTouchedFile(true);
                    setFile(e.target.value);
                  }}
                />
                <datalist id="content-files">
                  {index.files.map((f) => (
                    <option key={f} value={f} />
                  ))}
                </datalist>
              </div>
            </div>
          ) : (
            <div className="kindhint">Select a kind above to name and place the new def.</div>
          )}

          {error && <div className="saveresult bad">{error}</div>}
        </div>

        <div className="modal-foot">
          <button className="ghost" onClick={onClose}>Cancel</button>
          <button className="save" disabled={busy || !kind || id.trim() === ""} onClick={create}>
            {busy ? "Creating…" : "Create & edit"}
          </button>
        </div>
      </div>
    </div>
  );
}
