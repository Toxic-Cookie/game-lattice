import { MarkerType, type Edge, type Node } from "reactflow";
import type { JsonObject } from "./api.ts";

// One generic node shape drives every graph kind; adapters fill what's relevant.
export interface GraphNodeData {
  title: string;
  tag?: string;
  tagColor?: string;
  subtitle?: string;
  line?: string;
  meta?: string[];
  start?: boolean;
  terminal?: boolean;
}
export type GraphNode = Node<GraphNodeData>;
export interface Graph {
  nodes: GraphNode[];
  edges: Edge[];
}
export type Adapter = (def: JsonObject) => Graph;

const COL = 320;
const ROW = 150;
const EDGE = "#3a4256";
const ACCENT = "#5b8cff";
const GOLD = "#d2a23b";
const GREEN = "#3fb950";
const PURPLE = "#a371f7";

interface RawNode {
  id: string;
  data: GraphNodeData;
}

// BFS layering from the roots; unreachable nodes are pushed past the deepest rank.
function layout(ids: string[], links: { source: string; target: string }[], roots: string[]): Map<string, { x: number; y: number }> {
  const set = new Set(ids);
  const adj = new Map<string, string[]>();
  for (const id of ids) adj.set(id, []);
  for (const l of links) if (adj.has(l.source)) adj.get(l.source)!.push(l.target);

  const rank = new Map<string, number>();
  const queue = [...roots];
  for (const r of roots) rank.set(r, 0);
  while (queue.length) {
    const k = queue.shift()!;
    for (const t of adj.get(k) ?? []) {
      if (set.has(t) && !rank.has(t)) {
        rank.set(t, rank.get(k)! + 1);
        queue.push(t);
      }
    }
  }
  let max = 0;
  for (const r of rank.values()) max = Math.max(max, r);
  for (const id of ids) if (!rank.has(id)) rank.set(id, ++max);

  const usedPerRank = new Map<number, number>();
  const pos = new Map<string, { x: number; y: number }>();
  for (const id of ids) {
    const r = rank.get(id)!;
    const row = usedPerRank.get(r) ?? 0;
    usedPerRank.set(r, row + 1);
    pos.set(id, { x: r * COL, y: row * ROW });
  }
  return pos;
}

function finish(raw: RawNode[], edges: Edge[], roots: string[]): Graph {
  const pos = layout(raw.map((n) => n.id), edges, roots);
  const nodes: GraphNode[] = raw.map((n) => ({ id: n.id, type: "card", position: pos.get(n.id) ?? { x: 0, y: 0 }, data: n.data }));
  return { nodes, edges };
}

// Edit metadata an editable adapter attaches to an edge so a graph rewire/delete
// can be mapped back to the exact place in the def JSON.
export interface EdgeData {
  kind: "option" | "next" | "transition";
  key: string;
  index?: number;
}

function edge(source: string, target: string, label: string, color = EDGE, dashed = false, animated = false, data?: EdgeData): Edge<EdgeData> {
  return {
    id: data ? `${data.kind}:${data.key}:${data.index ?? "n"}` : `${source}->${target}:${label}`,
    source,
    target,
    label: label || undefined,
    labelShowBg: !!label,
    animated,
    data,
    style: dashed ? { stroke: color, strokeDasharray: "5 4" } : { stroke: color },
    markerEnd: { type: MarkerType.ArrowClosed, color },
  };
}

// --- shared summarizers -----------------------------------------------------

/* eslint-disable @typescript-eslint/no-explicit-any */
function cond(c: any): string {
  if (c === null || typeof c !== "object") return String(c);
  switch (c.type) {
    case "AgentCondition": return c.condition;
    case "AgentMeta": return `is ${c.is}`;
    case "Any": return `any(${(c.conditions ?? []).map(cond).join(", ")})`;
    case "All": return `all(${(c.conditions ?? []).map(cond).join(", ")})`;
    case "Not": return `¬${cond(c.condition)}`;
    case "FlagEquals": return `${c.flag}=${c.value}`;
    case "BeliefEquals": return `${c.key}=${c.value}`;
    case "FormulaTrue": return String(c.formula);
    case "HasItem": return `has ${c.count ?? 1} ${c.item}`;
    case "HasTag": return `tag ${c.tag}`;
    case "NeedBelow": return `${c.need}<${c.threshold ?? 0.5}`;
    case "StatAtLeast": return `${c.stat}≥${c.value}`;
    case "UtilityAtLeast": return `${c.evaluator}≥${c.threshold ?? 0.5}`;
    default: return c.type ?? "?";
  }
}
const whenText = (arr: any): string => (Array.isArray(arr) ? arr.map(cond).join(" ∧ ") : "");
const predicates = (obj: any): string =>
  obj && typeof obj === "object" ? Object.entries(obj).map(([k, v]) => `${k}=${v}`).join(" ∧ ") : "";

function args(obj: any, exclude: string[]): string {
  return Object.entries(obj)
    .filter(([k, v]) => !exclude.includes(k) && (typeof v !== "object" || Array.isArray(v)))
    .map(([k, v]) => `${k}=${Array.isArray(v) ? `[${v.join(",")}]` : v}`)
    .join("  ");
}

// --- adapters ---------------------------------------------------------------

function dialogue(def: JsonObject): Graph {
  const raw = (def.nodes as any) ?? {};
  const keys = Object.keys(raw);
  const start = typeof def.start === "string" && raw[def.start] ? def.start : keys[0];
  const targets = (n: any): string[] => [...(n.options ?? []).map((o: any) => o.next).filter(Boolean), ...(n.next ? [n.next] : [])];

  const nodes: RawNode[] = [];
  const edges: Edge[] = [];
  for (const k of keys) {
    const n = raw[k];
    const meta: string[] = [];
    if (n.options?.length) meta.push(`${n.options.length} option${n.options.length === 1 ? "" : "s"}`);
    if (n.effects?.length) meta.push(`${n.effects.length} effect${n.effects.length === 1 ? "" : "s"}`);
    const terminal = targets(n).length === 0;
    if (terminal) meta.push("end");
    nodes.push({ id: k, data: { title: k, subtitle: n.speaker, line: n.line, meta, start: k === start, terminal } });

    (n.options ?? []).forEach((o: any, i: number) => {
      const gated = (o.conditions?.length ?? 0) > 0;
      if (o.next) edges.push(edge(k, o.next, o.text || "(option)", gated ? GOLD : ACCENT, gated, false, { kind: "option", key: k, index: i }));
    });
    if (n.next) edges.push(edge(k, n.next, "continue", "#6f7891", false, true, { kind: "next", key: k }));
  }
  return finish(nodes, edges, start ? [start] : []);
}

function btree(def: JsonObject): Graph {
  const nodes: RawNode[] = [];
  const edges: Edge[] = [];
  const walk = (node: any, id: string): void => {
    let data: GraphNodeData;
    let kids: any[] = [];
    if (node.children) {
      data = { tag: "composite", tagColor: ACCENT, title: node.node ?? "?" };
      kids = node.children;
    } else if (node.child) {
      data = {
        tag: "decorator",
        tagColor: GOLD,
        title: node.node ?? "?",
        line: node.node === "ConditionGate" ? whenText(node.when) : node.seconds !== undefined ? `${node.seconds}s` : undefined,
      };
      kids = [node.child];
    } else if (node.task !== undefined) {
      data = { tag: "task", tagColor: GREEN, title: String(node.task), line: args(node, ["task"]), terminal: true };
    } else if (node.subtree !== undefined) {
      data = { tag: "subtree", tagColor: PURPLE, title: String(node.subtree), terminal: true };
    } else if (node.condition !== undefined) {
      data = { tag: "condition", tagColor: GOLD, title: cond(node.condition), terminal: true };
    } else {
      data = { title: "unknown" };
    }
    nodes.push({ id, data });
    kids.forEach((c, i) => {
      const childId = `${id}.${i}`;
      walk(c, childId);
      edges.push(edge(id, childId, kids.length > 1 ? String(i + 1) : ""));
    });
  };
  if (def.root) walk(def.root, "n0");
  return finish(nodes, edges, def.root ? ["n0"] : []);
}

function fsmbrain(def: JsonObject): Graph {
  const states = (def.states as any) ?? {};
  const keys = Object.keys(states);
  const initial = typeof def.initial === "string" && states[def.initial] ? def.initial : keys[0];
  const nodes: RawNode[] = [];
  const edges: Edge[] = [];
  for (const k of keys) {
    const s = states[k];
    nodes.push({ id: k, data: { tag: "state", tagColor: ACCENT, title: k, subtitle: s.steering?.type, start: k === initial } });
    (s.transitions ?? []).forEach((t: any, i: number) => {
      if (t.to) edges.push(edge(k, t.to, whenText(t.when) || "→", ACCENT, false, false, { kind: "transition", key: k, index: i }));
    });
  }
  return finish(nodes, edges, initial ? [initial] : []);
}

function htncompound(def: JsonObject): Graph {
  const nodes: RawNode[] = [];
  const edges: Edge[] = [];
  const root = "root";
  nodes.push({ id: root, data: { tag: "compound", tagColor: PURPLE, title: String(def.id ?? "compound"), start: true } });
  ((def.methods as any[]) ?? []).forEach((m, i) => {
    const mid = `m${i}`;
    nodes.push({
      id: mid,
      data: { tag: "method", tagColor: GOLD, title: m.name ?? `method ${i + 1}`, line: m.preconditions ? `if ${predicates(m.preconditions)}` : undefined, meta: [`#${i + 1}`] },
    });
    edges.push(edge(root, mid, String(i + 1)));
    (m.subtasks ?? []).forEach((st: any, j: number) => {
      const sid = `${mid}s${j}`;
      nodes.push({ id: sid, data: { tag: "ref", tagColor: GREEN, title: String(st), terminal: true } });
      edges.push(edge(mid, sid, String(j + 1)));
    });
  });
  return finish(nodes, edges, [root]);
}
/* eslint-enable @typescript-eslint/no-explicit-any */

export const adapters: Record<string, Adapter> = { dialogue, btree, fsmbrain, htncompound };
export const graphKinds = Object.keys(adapters);

// --- editing (keyed-node kinds only) ----------------------------------------

export interface EditableOps {
  /** Add an option/transition from source to target. */
  connect(def: JsonObject, source: string, target: string): JsonObject;
  /** Retarget an existing edge to a new node. */
  reconnect(def: JsonObject, data: EdgeData, target: string): JsonObject;
  /** Remove the option/transition the edge represents. */
  removeEdge(def: JsonObject, data: EdgeData): JsonObject;
  /** Add a fresh node; returns the new def (the node gets a generated key). */
  addNode(def: JsonObject): JsonObject;
  /** Delete a node and scrub references to it. */
  removeNode(def: JsonObject, id: string): JsonObject;
}

const clone = (def: JsonObject): JsonObject => structuredClone(def);

function uniqueKey(obj: Record<string, unknown>, prefix: string): string {
  for (let i = 1; ; i++) {
    const key = `${prefix}_${i}`;
    if (!(key in obj)) return key;
  }
}

/* eslint-disable @typescript-eslint/no-explicit-any */
export const editable: Record<string, EditableOps> = {
  dialogue: {
    connect(def, source, target) {
      const d = clone(def);
      const nodes = ((d as any).nodes ??= {});
      const n = (nodes[source] ??= {});
      (n.options ??= []).push({ text: "", next: target });
      return d;
    },
    reconnect(def, data, target) {
      const d = clone(def);
      const n = (d as any).nodes?.[data.key];
      if (!n) return d;
      if (data.kind === "next") n.next = target;
      else if (n.options?.[data.index!]) n.options[data.index!].next = target;
      return d;
    },
    removeEdge(def, data) {
      const d = clone(def);
      const n = (d as any).nodes?.[data.key];
      if (!n) return d;
      if (data.kind === "next") delete n.next;
      else n.options?.splice(data.index!, 1);
      return d;
    },
    addNode(def) {
      const d = clone(def);
      const nodes = ((d as any).nodes ??= {});
      const key = uniqueKey(nodes, "node");
      nodes[key] = { speaker: "", line: "" };
      if (!(d as any).start) (d as any).start = key;
      return d;
    },
    removeNode(def, id) {
      const d = clone(def);
      const nodes = (d as any).nodes ?? {};
      delete nodes[id];
      for (const k of Object.keys(nodes)) {
        const n = nodes[k];
        if (n.next === id) delete n.next;
        if (n.options) n.options = n.options.filter((o: any) => o.next !== id);
      }
      if ((d as any).start === id) (d as any).start = Object.keys(nodes)[0];
      return d;
    },
  },
  fsmbrain: {
    connect(def, source, target) {
      const d = clone(def);
      const states = ((d as any).states ??= {});
      const s = (states[source] ??= {});
      (s.transitions ??= []).push({ to: target, when: [] });
      return d;
    },
    reconnect(def, data, target) {
      const d = clone(def);
      const s = (d as any).states?.[data.key];
      if (s?.transitions?.[data.index!]) s.transitions[data.index!].to = target;
      return d;
    },
    removeEdge(def, data) {
      const d = clone(def);
      const s = (d as any).states?.[data.key];
      if (s?.transitions) s.transitions.splice(data.index!, 1);
      return d;
    },
    addNode(def) {
      const d = clone(def);
      const states = ((d as any).states ??= {});
      const key = uniqueKey(states, "state");
      states[key] = { steering: { type: "Idle" }, transitions: [] };
      if (!(d as any).initial) (d as any).initial = key;
      return d;
    },
    removeNode(def, id) {
      const d = clone(def);
      const states = (d as any).states ?? {};
      delete states[id];
      for (const k of Object.keys(states)) {
        const s = states[k];
        if (s.transitions) s.transitions = s.transitions.filter((t: any) => t.to !== id);
      }
      if ((d as any).initial === id) (d as any).initial = Object.keys(states)[0];
      return d;
    },
  },
};
/* eslint-enable @typescript-eslint/no-explicit-any */

export const editableKinds = Object.keys(editable);
