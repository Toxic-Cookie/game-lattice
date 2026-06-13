import type { Edge, Node } from "reactflow";
import type { Json, JsonObject } from "./api.ts";

export interface DialogueNodeData {
  key: string;
  speaker: string;
  line: string;
  isStart: boolean;
  effectCount: number;
  optionCount: number;
  terminal: boolean;
}

interface RawOption {
  text?: string;
  next?: string;
  conditions?: Json[];
  effects?: Json[];
}
interface RawNode {
  speaker?: string;
  line?: string;
  effects?: Json[];
  next?: string;
  options?: RawOption[];
}

const COL = 320;
const ROW = 150;

/** Map a DialogueTreeDef's JSON into a React Flow graph, laid out by BFS depth from the start node. */
export function dialogueToGraph(def: JsonObject): { nodes: Node<DialogueNodeData>[]; edges: Edge[] } {
  const raw = (def.nodes as Record<string, RawNode> | undefined) ?? {};
  const keys = Object.keys(raw);
  const start = typeof def.start === "string" && raw[def.start] ? def.start : keys[0];

  const targetsOf = (n: RawNode): string[] => [
    ...(n.options ?? []).map((o) => o.next).filter((t): t is string => !!t),
    ...(n.next ? [n.next] : []),
  ];

  // BFS rank from start; unreachable nodes get pushed past the deepest rank.
  const rank = new Map<string, number>();
  const queue: string[] = [];
  if (start) {
    rank.set(start, 0);
    queue.push(start);
  }
  while (queue.length) {
    const k = queue.shift()!;
    for (const t of targetsOf(raw[k])) {
      if (raw[t] && !rank.has(t)) {
        rank.set(t, rank.get(k)! + 1);
        queue.push(t);
      }
    }
  }
  let maxRank = 0;
  for (const r of rank.values()) maxRank = Math.max(maxRank, r);
  for (const k of keys) if (!rank.has(k)) rank.set(k, ++maxRank);

  const rowInRank = new Map<string, number>();
  const usedPerRank = new Map<number, number>();
  for (const k of keys) {
    const r = rank.get(k)!;
    const row = usedPerRank.get(r) ?? 0;
    rowInRank.set(k, row);
    usedPerRank.set(r, row + 1);
  }

  const nodes: Node<DialogueNodeData>[] = keys.map((k) => {
    const n = raw[k];
    return {
      id: k,
      type: "dialogue",
      position: { x: rank.get(k)! * COL, y: rowInRank.get(k)! * ROW },
      data: {
        key: k,
        speaker: n.speaker ?? "",
        line: n.line ?? "",
        isStart: k === start,
        effectCount: n.effects?.length ?? 0,
        optionCount: n.options?.length ?? 0,
        terminal: targetsOf(n).length === 0,
      },
    };
  });

  const edges: Edge[] = [];
  for (const k of keys) {
    const n = raw[k];
    (n.options ?? []).forEach((o, i) => {
      if (!o.next) return;
      const gated = (o.conditions?.length ?? 0) > 0;
      edges.push({
        id: `${k}-opt${i}`,
        source: k,
        target: o.next,
        label: o.text || "(option)",
        labelShowBg: true,
        style: gated ? { strokeDasharray: "5 4", stroke: "#d2a23b" } : { stroke: "#5b8cff" },
        markerEnd: { type: "arrowclosed" as never, color: gated ? "#d2a23b" : "#5b8cff" },
      });
    });
    if (n.next) {
      edges.push({
        id: `${k}-next`,
        source: k,
        target: n.next,
        label: "continue",
        animated: true,
        style: { stroke: "#6f7891" },
        markerEnd: { type: "arrowclosed" as never, color: "#6f7891" },
      });
    }
  }

  return { nodes, edges };
}
