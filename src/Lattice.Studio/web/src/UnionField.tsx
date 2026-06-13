import { useState } from "react";
import { RefPicker, type RefOption } from "./RefPicker.tsx";
import type { Json, JsonObject, PrimitiveDoc } from "./api.ts";

export type UnionKind = "effect" | "condition" | "task";

interface ArgSpec {
  name: string;
  optional: boolean;
  hint: string;
  refKind?: string;
}

// Parse a manifest arg signature like
//   "formula: damage amount (dice ok); stat?: stat def id (default stat_hp)"
// into structured fields. Complex args (objects/arrays) still parse to a name +
// hint; the raw-JSON escape covers anything the flat fields can't express.
function parseArgs(args: string): ArgSpec[] {
  if (!args || args.startsWith("(no args")) return [];
  return args
    .split(";")
    .map((part) => part.trim())
    .filter(Boolean)
    .map((part) => {
      const colon = part.indexOf(":");
      const rawName = colon >= 0 ? part.slice(0, colon).trim() : part.trim();
      const hint = colon >= 0 ? part.slice(colon + 1).trim() : "";
      const optional = rawName.endsWith("?");
      const name = optional ? rawName.slice(0, -1) : rawName;
      const refMatch = hint.match(/(\w+) def id/);
      return { name, optional, hint, refKind: refMatch?.[1] };
    })
    .filter((a) => /^\w+$/.test(a.name));
}

function coerce(text: string): Json {
  if (text === "true") return true;
  if (text === "false") return false;
  if (/^-?\d+(\.\d+)?$/.test(text)) return Number(text);
  return text;
}

function disc(kind: UnionKind): string {
  return kind === "task" ? "task" : "type";
}

interface EditorProps {
  value: JsonObject;
  kind: UnionKind;
  primitives: PrimitiveDoc[];
  optionsByKind: Record<string, RefOption[]>;
  onChange: (v: JsonObject) => void;
  onGoTo?: (id: string) => void;
}

export function UnionPayload({ value, kind, primitives, optionsByKind, onChange, onGoTo }: EditorProps) {
  const [raw, setRaw] = useState(false);
  const [rawText, setRawText] = useState("");
  const [rawError, setRawError] = useState<string | null>(null);

  const key = disc(kind);
  const type = (value[key] as string) ?? "";
  const prim = primitives.find((p) => p.type === type);
  const specs = prim ? parseArgs(prim.args) : [];

  const setArg = (name: string, v: Json | undefined) => {
    const next = { ...value };
    if (v === undefined || v === "") delete next[name];
    else next[name] = v;
    onChange(next);
  };

  const enterRaw = () => {
    setRawText(JSON.stringify(value, null, 2));
    setRawError(null);
    setRaw(true);
  };

  return (
    <div className="union">
      <div className="unionhead">
        <select
          className="uniontype"
          value={type}
          onChange={(e) => onChange({ [key]: e.target.value })}
        >
          {!type && <option value="">— pick {kind} —</option>}
          {primitives.map((p) => (
            <option key={p.type} value={p.type}>
              {p.type}
            </option>
          ))}
        </select>
        <button type="button" className={`rawtoggle ${raw ? "on" : ""}`} onClick={() => (raw ? setRaw(false) : enterRaw())}>
          {raw ? "fields" : "json"}
        </button>
      </div>

      {prim && !raw && <div className="primdoc">{prim.description}</div>}

      {raw ? (
        <>
          <textarea
            className={`rawjson ${rawError ? "invalid" : ""}`}
            value={rawText}
            spellCheck={false}
            rows={Math.min(12, rawText.split("\n").length + 1)}
            onChange={(e) => {
              setRawText(e.target.value);
              try {
                const parsed = JSON.parse(e.target.value);
                setRawError(null);
                onChange(parsed as JsonObject);
              } catch (err) {
                setRawError(String(err));
              }
            }}
          />
          {rawError && <span className="hint bad">invalid JSON</span>}
        </>
      ) : (
        prim && (
          <div className="unionargs">
            {specs.map((spec) => {
              const current = value[spec.name];
              return (
                <div className="arg" key={spec.name}>
                  <label>
                    {spec.name}
                    {!spec.optional && <span className="req">*</span>}
                    <span className="arghint">{spec.hint}</span>
                  </label>
                  {spec.refKind ? (
                    <RefPicker
                      value={(current as string) ?? ""}
                      refKind={spec.refKind}
                      optionsByKind={optionsByKind}
                      onChange={(v) => setArg(spec.name, v)}
                      onGoTo={onGoTo}
                    />
                  ) : (
                    <input
                      value={current === undefined ? "" : String(current)}
                      spellCheck={false}
                      onChange={(e) => setArg(spec.name, e.target.value === "" ? undefined : coerce(e.target.value))}
                    />
                  )}
                </div>
              );
            })}
            {prim.example && <code className="example" title="example">{prim.example}</code>}
          </div>
        )
      )}
    </div>
  );
}

interface ArrayProps {
  value: JsonObject[];
  kind: UnionKind;
  primitives: PrimitiveDoc[];
  optionsByKind: Record<string, RefOption[]>;
  onChange: (v: JsonObject[]) => void;
  onGoTo?: (id: string) => void;
}

export function UnionArray({ value, kind, primitives, optionsByKind, onChange, onGoTo }: ArrayProps) {
  const key = disc(kind);
  const update = (i: number, v: JsonObject) => onChange(value.map((x, j) => (j === i ? v : x)));
  const remove = (i: number) => onChange(value.filter((_, j) => j !== i));
  const add = () => onChange([...value, { [key]: primitives[0]?.type ?? "" }]);

  return (
    <div className="unionarray">
      {value.map((item, i) => (
        <div className="unionitem" key={i}>
          <button type="button" className="removeitem" title="remove" onClick={() => remove(i)}>
            ✕
          </button>
          <UnionPayload
            value={item}
            kind={kind}
            primitives={primitives}
            optionsByKind={optionsByKind}
            onChange={(v) => update(i, v)}
            onGoTo={onGoTo}
          />
        </div>
      ))}
      <button type="button" className="additem" onClick={add}>
        + add {kind}
      </button>
    </div>
  );
}
