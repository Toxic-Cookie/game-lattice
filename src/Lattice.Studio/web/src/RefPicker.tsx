import { useMemo, useRef, useState } from "react";

export interface RefOption {
  id: string;
  description: string | null;
  kind: string;
}

interface Props {
  value: string;
  refKind: string; // one or more kinds joined by "|"
  optionsByKind: Record<string, RefOption[]>;
  onChange: (v: string) => void;
  onGoTo?: (id: string) => void;
}

export function RefPicker({ value, refKind, optionsByKind, onChange, onGoTo }: Props) {
  const [open, setOpen] = useState(false);
  const [filter, setFilter] = useState("");
  const blurTimer = useRef<number | undefined>(undefined);

  const options = useMemo(() => {
    const kinds = refKind.split("|");
    return kinds.flatMap((k) => optionsByKind[k] ?? []);
  }, [refKind, optionsByKind]);

  const valid = value === "" || options.some((o) => o.id === value);

  const matches = useMemo(() => {
    const q = filter.trim().toLowerCase();
    const list = q
      ? options.filter((o) => o.id.toLowerCase().includes(q) || (o.description ?? "").toLowerCase().includes(q))
      : options;
    return list.slice(0, 50);
  }, [options, filter]);

  return (
    <div className="refpicker">
      <div className="refinput">
        <input
          className={valid ? "" : "invalid"}
          value={value}
          spellCheck={false}
          placeholder={`pick a ${refKind} def…`}
          onChange={(e) => {
            onChange(e.target.value);
            setFilter(e.target.value);
            setOpen(true);
          }}
          onFocus={() => {
            setFilter("");
            setOpen(true);
          }}
          onBlur={() => {
            blurTimer.current = window.setTimeout(() => setOpen(false), 120);
          }}
        />
        {!valid && value !== "" && <span className="dangling" title="no def with this id">!</span>}
        {valid && value !== "" && onGoTo && (
          <button
            type="button"
            className="goto"
            title={`go to ${value}`}
            onMouseDown={(e) => e.preventDefault()}
            onClick={() => onGoTo(value)}
          >
            ↗
          </button>
        )}
      </div>

      {open && matches.length > 0 && (
        <ul className="refmenu">
          {matches.map((o) => (
            <li
              key={o.id}
              className={o.id === value ? "active" : ""}
              onMouseDown={(e) => {
                e.preventDefault();
                window.clearTimeout(blurTimer.current);
                onChange(o.id);
                setOpen(false);
              }}
            >
              <code>{o.id}</code>
              {o.description && <span className="refdesc">{o.description}</span>}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
