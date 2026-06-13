import { useEffect, useMemo, useState, type ReactNode } from "react";
import { api, type Json, type JsonObject, type JsonSchema, type SaveResult } from "./api.ts";

interface Props {
  id: string;
  schemas: Record<string, JsonSchema>;
  onClose: () => void;
  onSaved: () => void;
}

const PRIMITIVE = new Set(["string", "number", "integer", "boolean"]);

function typeOf(schema: JsonSchema | undefined): string | undefined {
  if (!schema) return undefined;
  return Array.isArray(schema.type) ? schema.type.find((t) => t !== "null") : schema.type;
}

export function Editor({ id, schemas, onClose, onSaved }: Props) {
  const [original, setOriginal] = useState<JsonObject | null>(null);
  const [draft, setDraft] = useState<JsonObject | null>(null);
  const [kind, setKind] = useState<string>("");
  const [file, setFile] = useState<string>("");
  const [result, setResult] = useState<SaveResult | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setResult(null);
    setError(null);
    api
      .def(id)
      .then((p) => {
        setOriginal(p.def);
        setDraft(structuredClone(p.def));
        setKind(p.kind);
        setFile(p.sourceFile);
      })
      .catch((e) => setError(String(e)));
  }, [id]);

  const schema = schemas[kind];

  const changedKeys = useMemo(() => {
    if (!original || !draft) return [];
    return Object.keys(draft).filter((k) => JSON.stringify(draft[k]) !== JSON.stringify(original[k]));
  }, [original, draft]);

  if (error) return <Panel onClose={onClose} title={id}><div className="fatal">{error}</div></Panel>;
  if (!draft) return <Panel onClose={onClose} title={id}><div className="loading">Loading…</div></Panel>;

  const set = (key: string, value: Json) => setDraft({ ...draft, [key]: value });

  const save = async () => {
    setSaving(true);
    setError(null);
    try {
      const r = await api.save(id, draft);
      setResult(r);
      if (r.written || r.status === "unchanged") {
        setOriginal(structuredClone(draft));
        onSaved();
      }
    } catch (e) {
      setError(String(e));
    } finally {
      setSaving(false);
    }
  };

  return (
    <Panel onClose={onClose} title={id} subtitle={`${kind} · ${file}`}>
      <div className="fields">
        {Object.keys(draft).map((key) => (
          <Field
            key={key}
            name={key}
            value={draft[key]}
            schema={schema?.properties?.[key]}
            readOnly={key === "id" || key === "type"}
            changed={changedKeys.includes(key)}
            onChange={(v) => set(key, v)}
          />
        ))}
      </div>

      <div className="editor-footer">
        <div className="changesummary">
          {changedKeys.length === 0 ? "No changes" : `${changedKeys.length} changed: ${changedKeys.join(", ")}`}
        </div>
        <button className="save" disabled={saving || changedKeys.length === 0} onClick={save}>
          {saving ? "Saving…" : "Save"}
        </button>
      </div>

      {result && (
        <div className={`saveresult ${result.validation?.ok ? "ok" : "bad"}`}>
          {result.status === "written" ? "Saved." : "No change written."}{" "}
          {result.validation &&
            (result.validation.ok
              ? "Content valid ✓"
              : `${result.validation.errors.length} validation error(s):`)}
          {result.validation && !result.validation.ok && (
            <ul>
              {result.validation.errors.map((e, i) => (
                <li key={i}>{e}</li>
              ))}
            </ul>
          )}
        </div>
      )}
    </Panel>
  );
}

function Field({
  name,
  value,
  schema,
  readOnly,
  changed,
  onChange,
}: {
  name: string;
  value: Json;
  schema: JsonSchema | undefined;
  readOnly: boolean;
  changed: boolean;
  onChange: (v: Json) => void;
}) {
  const t = typeOf(schema);
  const ref = schema?.["x-lattice-ref"];
  const label = (
    <label>
      {name}
      {ref && <span className="reftag" title={`reference to a ${ref} def`}>→ {ref}</span>}
      {changed && <span className="changedot" title="changed" />}
    </label>
  );

  if (readOnly) {
    return (
      <div className="field">
        {label}
        <input className="ro" value={String(value)} readOnly />
      </div>
    );
  }

  if (t === "boolean") {
    return (
      <div className="field">
        {label}
        <input type="checkbox" checked={value === true} onChange={(e) => onChange(e.target.checked)} />
      </div>
    );
  }

  if (t === "number" || t === "integer") {
    return (
      <div className="field">
        {label}
        <input
          type="number"
          value={value === null || value === undefined ? "" : Number(value)}
          onChange={(e) => onChange(e.target.value === "" ? null : Number(e.target.value))}
        />
      </div>
    );
  }

  if (t === "string") {
    const long = name === "description" || name === "formula";
    return (
      <div className="field">
        {label}
        {long ? (
          <textarea value={(value as string) ?? ""} onChange={(e) => onChange(e.target.value)} rows={2} />
        ) : (
          <input value={(value as string) ?? ""} onChange={(e) => onChange(e.target.value)} />
        )}
      </div>
    );
  }

  // array of primitives → editable comma-separated tokens
  if (t === "array" && PRIMITIVE.has(typeOf(schema?.items) ?? "")) {
    const itemType = typeOf(schema?.items);
    const arr = Array.isArray(value) ? value : [];
    return (
      <div className="field">
        {label}
        <input
          value={arr.join(", ")}
          placeholder="comma, separated"
          onChange={(e) => {
            const parts = e.target.value
              .split(",")
              .map((s) => s.trim())
              .filter((s) => s.length > 0);
            onChange(itemType === "number" || itemType === "integer" ? parts.map(Number) : parts);
          }}
        />
      </div>
    );
  }

  // objects, arrays of objects, unions: read-only in M8.2 (structured editing arrives in M8.3/M8.4)
  return (
    <div className="field">
      {label}
      <pre className="ro json">{JSON.stringify(value, null, 2)}</pre>
      <span className="hint">structured field — edited in a later phase</span>
    </div>
  );
}

function Panel({
  title,
  subtitle,
  onClose,
  children,
}: {
  title: string;
  subtitle?: string;
  onClose: () => void;
  children: ReactNode;
}) {
  return (
    <aside className="editor">
      <header className="editor-head">
        <div>
          <div className="editor-title">{title}</div>
          {subtitle && <div className="editor-sub">{subtitle}</div>}
        </div>
        <button className="close" onClick={onClose} title="Close">
          ✕
        </button>
      </header>
      <div className="editor-body">{children}</div>
    </aside>
  );
}
