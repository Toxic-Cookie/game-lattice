import { useEffect, useMemo, useState } from "react";
import ReactFlow, { Background, Controls, Handle, MiniMap, Position, type NodeProps } from "reactflow";
import "reactflow/dist/style.css";
import { api, type JsonObject } from "./api.ts";
import { adapters, type GraphNodeData } from "./graph.ts";

const nodeTypes = { card: GraphCard };

interface Props {
  id: string;
  onClose: () => void;
}

export function GraphView({ id, onClose }: Props) {
  const [def, setDef] = useState<JsonObject | null>(null);
  const [kind, setKind] = useState<string>("");
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api
      .def(id)
      .then((p) => {
        setDef(p.def);
        setKind(p.kind);
      })
      .catch((e) => setError(String(e)));
  }, [id]);

  const graph = useMemo(() => {
    const adapter = adapters[kind];
    return def && adapter ? adapter(def) : { nodes: [], edges: [] };
  }, [def, kind]);

  return (
    <div className="graph-overlay">
      <header className="graph-head">
        <div className="graph-title">
          <span className="kindtag">{kind}</span> <code>{id}</code>
          <span className="muted"> · {graph.nodes.length} nodes</span>
        </div>
        <button className="close" onClick={onClose} title="Close graph">✕</button>
      </header>
      <div className="graph-canvas">
        {error ? (
          <div className="fatal">{error}</div>
        ) : (
          <ReactFlow
            nodes={graph.nodes}
            edges={graph.edges}
            nodeTypes={nodeTypes}
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
