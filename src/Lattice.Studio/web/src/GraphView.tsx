import { useEffect, useMemo, useState } from "react";
import ReactFlow, { Background, Controls, Handle, MiniMap, Position, type NodeProps } from "reactflow";
import "reactflow/dist/style.css";
import { api, type JsonObject } from "./api.ts";
import { dialogueToGraph, type DialogueNodeData } from "./dialogueGraph.ts";

const nodeTypes = { dialogue: DialogueCard };

interface Props {
  id: string;
  onClose: () => void;
}

export function GraphView({ id, onClose }: Props) {
  const [def, setDef] = useState<JsonObject | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api.def(id).then((p) => setDef(p.def)).catch((e) => setError(String(e)));
  }, [id]);

  const graph = useMemo(() => (def ? dialogueToGraph(def) : { nodes: [], edges: [] }), [def]);

  return (
    <div className="graph-overlay">
      <header className="graph-head">
        <div className="graph-title">
          <span className="kindtag">dialogue</span> <code>{id}</code>
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
            minZoom={0.2}
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

function DialogueCard({ data }: NodeProps<DialogueNodeData>) {
  return (
    <div className={`dnode ${data.isStart ? "start" : ""} ${data.terminal ? "terminal" : ""}`}>
      <Handle type="target" position={Position.Left} />
      <div className="dnode-head">
        {data.isStart && <span className="startbadge">start</span>}
        <span className="dnode-key">{data.key}</span>
        <span className="dnode-speaker">{data.speaker}</span>
      </div>
      <div className="dnode-line">{data.line || <span className="muted">(no line)</span>}</div>
      <div className="dnode-foot">
        {data.optionCount > 0 && <span>{data.optionCount} option{data.optionCount === 1 ? "" : "s"}</span>}
        {data.effectCount > 0 && <span>{data.effectCount} effect{data.effectCount === 1 ? "" : "s"}</span>}
        {data.terminal && <span className="endtag">end</span>}
      </div>
      <Handle type="source" position={Position.Right} />
    </div>
  );
}
