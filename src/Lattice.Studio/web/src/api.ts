// Typed client for the Lattice.Studio read-only API (plan/08, M8.1).

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

async function getJson<T>(path: string): Promise<T> {
  const res = await fetch(path);
  if (!res.ok) throw new Error(`${path} → ${res.status} ${res.statusText}`);
  return (await res.json()) as T;
}

export const api = {
  content: () => getJson<ContentIndex>("/api/content"),
  validate: () => getJson<ValidationResult>("/api/validate"),
};
