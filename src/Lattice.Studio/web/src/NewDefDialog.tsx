import { useMemo, useState } from "react";
import { api, type ContentIndex } from "./api.ts";

interface Props {
  kinds: string[];
  index: ContentIndex;
  onClose: () => void;
  onCreated: (id: string) => void;
}

export function NewDefDialog({ kinds, index, onClose, onCreated }: Props) {
  const [kind, setKind] = useState(kinds[0] ?? "");
  const [id, setId] = useState("");
  const [file, setFile] = useState("");
  const [touchedFile, setTouchedFile] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  // Majority file for the chosen kind (the routing the backend would pick).
  const suggestedFile = useMemo(() => {
    const counts = new Map<string, number>();
    for (const d of index.defs) if (d.kind === kind && d.sourceFile) counts.set(d.sourceFile, (counts.get(d.sourceFile) ?? 0) + 1);
    let best = "";
    let max = 0;
    for (const [f, n] of counts) if (n > max) ((max = n), (best = f));
    return best || `${kind}.json`;
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
      <div className="modal" onClick={(e) => e.stopPropagation()}>
        <header className="modal-head">
          <span>New def</span>
          <button className="close" onClick={onClose}>✕</button>
        </header>

        <div className="modal-body">
          <div className="field">
            <label>kind</label>
            <select value={kind} onChange={(e) => setKind(e.target.value)}>
              {kinds.map((k) => (
                <option key={k} value={k}>{k}</option>
              ))}
            </select>
          </div>

          <div className="field">
            <label>id</label>
            <input
              value={id}
              spellCheck={false}
              placeholder={`${kind}_…`}
              autoFocus
              onChange={(e) => setId(e.target.value)}
            />
          </div>

          <div className="field">
            <label>
              file <span className="muted">· defaults to where {kind} defs live</span>
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

          {error && <div className="saveresult bad">{error}</div>}
        </div>

        <div className="modal-foot">
          <button className="ghost" onClick={onClose}>Cancel</button>
          <button className="save" disabled={busy || id.trim() === ""} onClick={create}>
            {busy ? "Creating…" : "Create & edit"}
          </button>
        </div>
      </div>
    </div>
  );
}
