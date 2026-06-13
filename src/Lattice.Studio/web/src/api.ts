// Typed client for the Lattice.Studio API (plan/08, M8.1–M8.2).

export interface DefIndexEntry {
  id: string;
  kind: string;
  description: string | null;
  inherits: string | null;
  sourceFile: string | null;
}

export interface ContentIndex {
  count: number;
  defs: DefIndexEntry[];
  files: string[];
}

export interface ValidationResult {
  ok: boolean;
  defs: number;
  files: number;
  errors: string[];
  warnings: string[];
}

// A JSON value as delivered by the API (def payloads are arbitrary JSON objects).
export type Json = string | number | boolean | null | Json[] | { [k: string]: Json };
export type JsonObject = { [k: string]: Json };

export interface DefPayload {
  kind: string;
  sourceFile: string;
  def: JsonObject;
}

// A minimal JSON Schema view (only the parts the form renderer reads).
export interface JsonSchema {
  type?: string | string[];
  const?: Json;
  title?: string;
  description?: string;
  properties?: Record<string, JsonSchema>;
  items?: JsonSchema;
  required?: string[];
  "x-lattice-ref"?: string;
  "x-lattice-union"?: string;
}

export interface SchemaBundle {
  kinds: Record<string, JsonSchema>;
  combined: JsonSchema;
}

// /api/catalog — every def by kind plus the documented primitive vocabularies.
export interface PrimitiveDoc {
  type: string;
  description: string;
  args: string;
  example: string;
}
export interface CatalogKind {
  kind: string;
  entries: { id: string; description: string | null; inherits: string | null }[];
}
export interface Catalog {
  defs: CatalogKind[];
  effects: PrimitiveDoc[];
  conditions: PrimitiveDoc[];
  tasks: PrimitiveDoc[];
  steering: PrimitiveDoc[];
}

export interface SaveResult {
  status: "written" | "unchanged" | "not_found" | "error";
  written: boolean;
  validation: ValidationResult | null;
  error?: string;
}

export interface LiveStatus {
  defs: number;
  reloads: number;
  lastReloadUtc: string | null;
  lastReloaded: string[];
  healthy: boolean;
  log: { time: string; level: string; message: string }[];
}

async function getJson<T>(path: string): Promise<T> {
  const res = await fetch(path);
  if (!res.ok) throw new Error(`${path} → ${res.status} ${res.statusText}`);
  return (await res.json()) as T;
}

export const api = {
  content: () => getJson<ContentIndex>("/api/content"),
  validate: () => getJson<ValidationResult>("/api/validate"),
  live: () => getJson<LiveStatus>("/api/live"),
  schemas: () => getJson<SchemaBundle>("/api/schemas"),
  catalog: () => getJson<Catalog>("/api/catalog"),
  def: (id: string) => getJson<DefPayload>(`/api/content/def/${encodeURIComponent(id)}`),
  save: async (id: string, def: JsonObject): Promise<SaveResult> => {
    const res = await fetch(`/api/content/def/${encodeURIComponent(id)}`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(def),
    });
    const body = (await res.json()) as SaveResult & { error?: string };
    if (!res.ok) throw new Error(body.error ?? `${res.status} ${res.statusText}`);
    return body;
  },
  create: async (def: JsonObject, file?: string): Promise<SaveResult> => {
    const res = await fetch("/api/content/def", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ def, file }),
    });
    const body = (await res.json()) as SaveResult & { error?: string };
    if (!res.ok) throw new Error(body.error ?? `${res.status} ${res.statusText}`);
    return body;
  },
};
