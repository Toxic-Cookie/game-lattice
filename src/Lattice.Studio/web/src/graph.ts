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

function edge(source: string, target: string, label: string, color = EDGE, dashed = false, animated = false): Edge {
  return {
    id: `${source}->${target}:${label}`,
    source,
    target,
    label: label || undefined,
    labelShowBg: !!label,
    animated,
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

    (n.options ?? []).forEach((o: any) => {
      if (o.next) edges.push(edge(k, o.next, o.text || "(option)", (o.conditions?.length ?? 0) > 0 ? GOLD : ACCENT, (o.conditions?.length ?? 0) > 0));
    });
    if (n.next) edges.push(edge(k, n.next, "continue", "#6f7891", false, true));
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
    (s.transitions ?? []).forEach((t: any) => {
      if (t.to) edges.push(edge(k, t.to, whenText(t.when) || "→", ACCENT));
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
