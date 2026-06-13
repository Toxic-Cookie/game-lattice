import { useEffect, useMemo, useState, type ReactNode } from "react";
import { api, type Json, type JsonObject, type JsonSchema, type PrimitiveDoc, type SaveResult } from "./api.ts";
import { RefPicker, type RefOption } from "./RefPicker.tsx";
import { UnionArray, UnionPayload, type UnionKind } from "./UnionField.tsx";

interface Props {
  id: string;
  schemas: Record<string, JsonSchema>;
  optionsByKind: Record<string, RefOption[]>;
  unions: Record<UnionKind, PrimitiveDoc[]>;
  onClose: () => void;
  onSaved: () => void;
  onGoTo: (id: string) => void;
}

const PRIMITIVE = new Set(["string", "number", "integer", "boolean"]);

function typeOf(schema: JsonSchema | undefined): string | undefined {
  if (!schema) return undefined;
  return Array.isArray(schema.type) ? schema.type.find((t) => t !== "null") : schema.type;
}

export function Editor({ id, schemas, optionsByKind, unions, onClose, onSaved, onGoTo }: Props) {
  const [original, setOriginal] = useState<JsonObject | null>(null);
  const [draft, setDraft] = useState<JsonObject | null>(null);
  const [kind, setKind] = useState<string>("");
  const [file, setFile] = useState<string>("");
  const [parent, setParent] = useState<JsonObject | null>(null);
  const [result, setResult] = useState<SaveResult | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setResult(null);
    setError(null);
    setParent(null);
    api
      .def(id)
      .then((p) => {
        setOriginal(p.def);
        setDraft(structuredClone(p.def));
        setKind(p.kind);
        setFile(p.sourceFile);
        const inherits = p.def["inherits"];
        if (typeof inherits === "string") {
          api.def(inherits).then((par) => setParent(par.def)).catch(() => {});
        }
      })
      .catch((e) => setError(String(e)));
  }, [id]);

  const schema = schemas[kind];

  const changedKeys = useMemo(() => {
    if (!original || !draft) return [];
    return Object.keys(draft).filter((k) => JSON.stringify(draft[k]) !== JSON.stringify(original[k]));
  }, [original, draft]);

  const inheritedKeys = useMemo(() => {
    if (!parent || !draft) return [];
    return Object.keys(parent).filter((k) => !(k in draft) && k !== "id" && k !== "type" && k !== "description");
  }, [parent, draft]);

  if (error) return <Panel onClose={onClose} title={id}><div className="fatal">{error}</div></Panel>;
  if (!draft) return <Panel onClose={onClose} title={id}><div className="loading">Loading…</div></Panel>;

  const set = (key: string, value: Json) => setDraft({ ...draft, [key]: value });

  const clone = async () => {
    const newId = window.prompt("New def id", `${id}_copy`)?.trim();
    if (!newId) return;
    setError(null);
    try {
      await api.create({ ...draft, id: newId }, file);
      onSaved();
      onGoTo(newId);
    } catch (e) {
      setError(String(e instanceof Error ? e.message : e));
    }
  };

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
    <Panel
      onClose={onClose}
      title={id}
      subtitle={`${kind} · ${file}`}
      actions={<button className="clone" onClick={clone} title="clone this def">⎘ clone</button>}
    >
      <div className="fields">
        {Object.keys(draft).map((key) => (
          <Field
            key={key}
            name={key}
            value={draft[key]}
            schema={schema?.properties?.[key]}
            readOnly={key === "id" || key === "type"}
            changed={changedKeys.includes(key)}
            optionsByKind={optionsByKind}
            unions={unions}
            onGoTo={onGoTo}
            onChange={(v) => set(key, v)}
          />
        ))}
      </div>

      {inheritedKeys.length > 0 && parent && (
        <details className="inherited">
          <summary>
            inherited from <code>{draft["inherits"] as string}</code>
            <button
              type="button"
              className="goto"
              title="go to parent"
              onClick={(e) => {
                e.preventDefault();
                onGoTo(draft["inherits"] as string);
              }}
            >
              ↗
            </button>
            <span className="muted"> · {inheritedKeys.length} field(s)</span>
          </summary>
          {inheritedKeys.map((k) => (
            <div className="field" key={k}>
              <label>{k}</label>
              <pre className="ro json">{JSON.stringify(parent[k], null, 2)}</pre>
            </div>
          ))}
        </details>
      )}

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
            (result.validation.ok ? "Content valid ✓" : `${result.validation.errors.length} validation error(s):`)}
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

interface FieldProps {
  name: string;
  value: Json;
  schema: JsonSchema | undefined;
  readOnly: boolean;
  changed: boolean;
  optionsByKind: Record<string, RefOption[]>;
  unions: Record<UnionKind, PrimitiveDoc[]>;
  onGoTo: (id: string) => void;
  onChange: (v: Json) => void;
}

function Field(p: FieldProps) {
  const { name, value, schema, readOnly, changed, optionsByKind, unions, onGoTo, onChange } = p;
  const t = typeOf(schema);
  const ref = schema?.["x-lattice-ref"];
  const union = schema?.["x-lattice-union"] as UnionKind | undefined;
  const itemUnion = schema?.items?.["x-lattice-union"] as UnionKind | undefined;
  const itemRef = schema?.items?.["x-lattice-ref"];
  const itemType = typeOf(schema?.items);

  const label = (
    <label>
      {name}
      {ref && <span className="reftag" title={`reference to a ${ref} def`}>→ {ref}</span>}
      {(union || itemUnion) && <span className="reftag" title={`${union ?? itemUnion} primitive`}>{union ?? itemUnion}</span>}
      {changed && <span className="changedot" title="changed" />}
    </label>
  );

  const field = (control: ReactNode) => (
    <div className="field">
      {label}
      {schema?.description && <span className="fieldhint">{schema.description}</span>}
      {control}
    </div>
  );

  if (readOnly) return field(<input className="ro" value={String(value)} readOnly />);

  if (ref && t === "string") {
    return field(
      <RefPicker value={(value as string) ?? ""} refKind={ref} optionsByKind={optionsByKind} onChange={onChange} onGoTo={onGoTo} />,
    );
  }

  if (union && (t === "object" || t === undefined)) {
    const obj = value && typeof value === "object" && !Array.isArray(value) ? (value as JsonObject) : {};
    return field(
      <UnionPayload value={obj} kind={union} primitives={unions[union]} optionsByKind={optionsByKind} onChange={onChange} onGoTo={onGoTo} />,
    );
  }

  if (t === "array" && itemUnion) {
    const arr = (Array.isArray(value) ? value : []) as JsonObject[];
    return field(
      <UnionArray value={arr} kind={itemUnion} primitives={unions[itemUnion]} optionsByKind={optionsByKind} onChange={onChange} onGoTo={onGoTo} />,
    );
  }

  if (t === "array" && itemRef && itemType === "string") {
    const arr = (Array.isArray(value) ? value : []) as string[];
    return field(
      <div className="refarray">
        {arr.map((v, i) => (
          <div className="refrow" key={i}>
            <RefPicker
              value={v}
              refKind={itemRef}
              optionsByKind={optionsByKind}
              onChange={(nv) => onChange(arr.map((x, j) => (j === i ? nv : x)))}
              onGoTo={onGoTo}
            />
            <button type="button" className="removeitem" onClick={() => onChange(arr.filter((_, j) => j !== i))}>
              ✕
            </button>
          </div>
        ))}
        <button type="button" className="additem" onClick={() => onChange([...arr, ""])}>
          + add {itemRef}
        </button>
      </div>,
    );
  }

  if (t === "boolean") {
    return field(<input type="checkbox" checked={value === true} onChange={(e) => onChange(e.target.checked)} />);
  }

  if (t === "number" || t === "integer") {
    return field(
      <input
        type="number"
        value={value === null || value === undefined ? "" : Number(value)}
        onChange={(e) => onChange(e.target.value === "" ? null : Number(e.target.value))}
      />,
    );
  }

  if (t === "string") {
    const long = name === "description" || name === "formula";
    return field(
      long ? (
        <textarea value={(value as string) ?? ""} onChange={(e) => onChange(e.target.value)} rows={2} />
      ) : (
        <input value={(value as string) ?? ""} onChange={(e) => onChange(e.target.value)} />
      ),
    );
  }

  if (t === "array" && PRIMITIVE.has(itemType ?? "")) {
    const arr = Array.isArray(value) ? value : [];
    return field(
      <input
        value={arr.join(", ")}
        placeholder="comma, separated"
        onChange={(e) => {
          const parts = e.target.value.split(",").map((s) => s.trim()).filter(Boolean);
          onChange(itemType === "number" || itemType === "integer" ? parts.map(Number) : parts);
        }}
      />,
    );
  }

  // Anything else (nested objects, arrays of concrete objects): editable raw JSON, validated on save.
  return field(<RawJson value={value} onChange={onChange} />);
}

function RawJson({ value, onChange }: { value: Json; onChange: (v: Json) => void }) {
  const [text, setText] = useState(() => JSON.stringify(value, null, 2));
  const [bad, setBad] = useState(false);
  return (
    <>
      <textarea
        className={`rawjson ${bad ? "invalid" : ""}`}
        value={text}
        spellCheck={false}
        rows={Math.min(14, text.split("\n").length + 1)}
        onChange={(e) => {
          setText(e.target.value);
          try {
            onChange(JSON.parse(e.target.value) as Json);
            setBad(false);
          } catch {
            setBad(true);
          }
        }}
      />
      {bad && <span className="hint bad">invalid JSON — not applied</span>}
    </>
  );
}

function Panel({
  title,
  subtitle,
  onClose,
  actions,
  children,
}: {
  title: string;
  subtitle?: string;
  onClose: () => void;
  actions?: ReactNode;
  children: ReactNode;
}) {
  return (
    <aside className="editor">
      <header className="editor-head">
        <div>
          <div className="editor-title">{title}</div>
          {subtitle && <div className="editor-sub">{subtitle}</div>}
        </div>
        <div className="editor-actions">
          {actions}
          <button className="close" onClick={onClose} title="Close">
            ✕
          </button>
        </div>
      </header>
      <div className="editor-body">{children}</div>
    </aside>
  );
}
