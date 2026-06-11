using System.Text.Json;
using Lattice.Core.Content;

namespace Lattice.Ai.Defs;

/// <summary>
/// An HTN compound task (ch04, HZD case study): ordered methods, each a
/// precondition-gated recipe of subtasks. Subtasks reference either another
/// compound or a <see cref="GoapActionDef"/> — HTN primitives ARE GOAP
/// actions, so both planners share one action vocabulary and one execution
/// layer. Designers/LLMs author the methods; that's the control-vs-emergence
/// dial (ch01 §1.6).
/// </summary>
public sealed class HtnCompoundDef : Def
{
    public List<Method> Methods { get; set; } = [];

    public override IEnumerable<DefReference> GetReferences()
    {
        foreach (var method in Methods)
        {
            foreach (var subtask in method.Subtasks)
            {
                yield return new DefReference(subtask, $"{Id}.methods");
            }
        }
    }

    public sealed class Method
    {
        /// <summary>Optional label for the decomposition trace.</summary>
        public string? Name { get; set; }

        /// <summary>Predicate map gating this method (evaluated against the *decomposition-time* state).</summary>
        public Dictionary<string, JsonElement>? Preconditions { get; set; }

        /// <summary>Ordered subtask def IDs (goapaction or htncompound).</summary>
        public List<string> Subtasks { get; set; } = [];
    }
}
