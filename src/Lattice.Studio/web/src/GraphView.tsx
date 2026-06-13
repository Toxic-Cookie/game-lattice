import { useEffect, useState } from "react";
import ReactFlow, {
  Background,
  Controls,
  Handle,
  MiniMap,
  Position,
  useEdgesState,
  useNodesState,
  type Connection,
  type Edge,
  type NodeProps,
} from "reactflow";
import "reactflow/dist/style.css";
import { api, type JsonObject, type SaveResult } from "./api.ts";
import { adapters, domainKinds, editable, goapDomain, type EdgeData, type GraphNodeData } from "./graph.ts";

const nodeTypes = { card: GraphCard };

interface Props {
  id: string;
  onClose: () => void;
  onSaved: () => void;
}

export function GraphView({ id, onClose, onSaved }: Props) {
  const [def, setDef] = useState<JsonObject | null>(null);
  const [kind, setKind] = useState<string>("");
  const [error, setError] = useState<string | null>(null);
  const [dirty, setDirty] = useState(false);
  const [saving, setSaving] = useState(false);
  const [result, setResult] = useState<SaveResult | null>(null);

  const [nodes, setNodes, onNodesChange] = useNodesState([]);
  const [edges, setEdges, onEdgesChange] = useEdgesState([]);

  const ops = editable[kind];

  useEffect(() => {
    api
      .def(id)
      .then((p) => {
        setDef(p.def);
        setKind(p.kind);
      })
      .catch((e) => setError(String(e)));
  }, [id]);

  // Recompute the canvas whenever the def changes (structural edit or first load).
  useEffect(() => {
    if (!kind) return;
    // Whole-domain kinds (GOAP) span every def of the kind, not just this one.
    if (domainKinds.includes(kind)) {
      let cancelled = false;
      Promise.all([api.defsOfKind("goapaction"), api.defsOfKind("goapgoal")])
        .then(([acts, goals]) => {
          if (cancelled) return;
          const g = goapDomain(acts, goals, id);
          setNodes(g.nodes);
          setEdges(g.edges);
        })
        .catch((e) => setError(String(e)));
      return () => {
        cancelled = true;
      };
    }
    const adapter = adapters[kind];
    if (def && adapter) {
      const g = adapter(def);
      setNodes(g.nodes);
      setEdges(g.edges);
    }
  }, [def, kind, id, setNodes, setEdges]);

  const mutate = (next: JsonObject) => {
    setDef(next);
    setDirty(true);
    setResult(null);
  };

  const onConnect = (c: Connection) => {
    if (ops && def && c.source && c.target) mutate(ops.connect(def, c.source, c.target));
  };
  const onEdgeUpdate = (oldEdge: Edge, c: Connection) => {
    const data = oldEdge.data as EdgeData | undefined;
    if (ops && def && data && c.target) mutate(ops.reconnect(def, data, c.target));
  };
  const onEdgesDelete = (deleted: Edge[]) => {
    if (!ops || !def) return;
    mutate(deleted.reduce((d, e) => (e.data ? ops.removeEdge(d, e.data as EdgeData) : d), def));
  };
  const onNodesDelete = (deleted: { id: string }[]) => {
    if (!ops || !def) return;
    mutate(deleted.reduce((d, n) => ops.removeNode(d, n.id), def));
  };

  const save = async () => {
    if (!def) return;
    setSaving(true);
    try {
      const r = await api.save(id, def);
      setResult(r);
      setDirty(false);
      onSaved();
    } catch (e) {
      setError(String(e instanceof Error ? e.message : e));
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="graph-overlay">
      <header className="graph-head">
        <div className="graph-title">
          <span className="kindtag">{kind}</span> <code>{id}</code>
          <span className="muted"> · {nodes.length} nodes</span>
          {ops && dirty && <span className="dirtydot" title="unsaved changes" />}
        </div>
        <div className="graph-actions">
          {ops && (
            <>
              <span className="graphhint">drag a node's ● to link · drag a link end to rewire · select + Del to remove</span>
              <button className="additem" onClick={() => def && mutate(ops.addNode(def))}>+ node</button>
              <button className="save" disabled={!dirty || saving} onClick={save}>
                {saving ? "Saving…" : "Save"}
              </button>
            </>
          )}
          <button className="close" onClick={onClose} title="Close graph">✕</button>
        </div>
      </header>

      {result && !result.validation?.ok && (
        <div className="graph-errors">
          {result.validation?.errors.length} validation error(s): {result.validation?.errors[0]}
        </div>
      )}

      <div className="graph-canvas">
        {error ? (
          <div className="fatal">{error}</div>
        ) : (
          <ReactFlow
            nodes={nodes}
            edges={edges}
            nodeTypes={nodeTypes}
            onNodesChange={onNodesChange}
            onEdgesChange={onEdgesChange}
            onConnect={onConnect}
            onEdgeUpdate={ops ? onEdgeUpdate : undefined}
            onEdgesDelete={ops ? onEdgesDelete : undefined}
            onNodesDelete={ops ? onNodesDelete : undefined}
            nodesConnectable={!!ops}
            edgesUpdatable={!!ops}
            deleteKeyCode={ops ? ["Backspace", "Delete"] : null}
            fitView
            minZoom={0.15}
            proOptions={{ hideAttribution: true }}
          >
            <Background color="#222a38" gap={22} />
            <MiniMap pannable zoomable nodeColor="#1d212c" maskColor="rgba(8,10,14,0.7)" />
            <Controls showInteractive={false} />
          </ReactFlow>
        )}
      </div>
    </div>
  );
}

function GraphCard({ data }: NodeProps<GraphNodeData>) {
  return (
    <div className={`gnode ${data.start ? "start" : ""} ${data.terminal ? "terminal" : ""}`}>
      <Handle type="target" position={Position.Left} />
      <div className="gnode-head">
        {data.start && <span className="startbadge">start</span>}
        {data.tag && (
          <span className="gtag" style={{ color: data.tagColor, borderColor: data.tagColor }}>
            {data.tag}
          </span>
        )}
        <span className="gnode-title">{data.title}</span>
        {data.subtitle && <span className="gnode-sub">{data.subtitle}</span>}
      </div>
      {data.line && <div className="gnode-line">{data.line}</div>}
      {data.meta && data.meta.length > 0 && (
        <div className="gnode-meta">
          {data.meta.map((m, i) => (
            <span key={i}>{m}</span>
          ))}
        </div>
      )}
      <Handle type="source" position={Position.Right} />
    </div>
  );
}
