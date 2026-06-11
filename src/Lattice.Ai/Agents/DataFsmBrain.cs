using System.Numerics;
using System.Text.Json;
using Lattice.Ai.Defs;
using Lattice.Rpg.Conditions;
using Lattice.Rpg.Effects;

namespace Lattice.Ai.Agents;

/// <summary>
/// The rat-tier brain (ch07 §7.1): a JSON-defined FSM whose states pair a
/// steering primitive with condition-gated transitions. No planner
/// structures are ever allocated for these agents.
/// </summary>
public sealed class DataFsmBrain : IBrain
{
    private readonly FsmBrainDef _def;
    private string _state;
    private double _nextWanderAt;

    public DataFsmBrain(FsmBrainDef def)
    {
        _def = def;
        _state = def.Initial;
    }

    public string Kind => "fsm";

    public string State => _state;

    public string Describe() => $"fsm {_def.Id} state={_state}";

    public void Tick(AgentContext ctx, float dt)
    {
        if (!_def.States.TryGetValue(_state, out var state))
        {
            return;
        }

        // transitions first: the first one whose conditions all hold wins
        var conditionContext = new ConditionContext
        {
            Session = ctx.Session,
            Rpg = ctx.Ai.Rpg,
            Subject = ctx.Entity,
        };
        foreach (var transition in state.Transitions ?? [])
        {
            if (ctx.Ai.Rpg.Conditions.EvaluateAll(transition.When, conditionContext)
                && _def.States.ContainsKey(transition.To)
                && transition.To != _state)
            {
                ctx.Agent.AddTrace(ctx.Session.Tick, $"fsm {_state} -> {transition.To}");
                _state = transition.To;
                ctx.Agent.StopMoving();
                _nextWanderAt = 0;
                state = _def.States[_state];
                break;
            }
        }

        if (state.Steering is { } steering)
        {
            Steer(ctx, steering);
        }
    }

    private void Steer(AgentContext ctx, JsonElement steering)
    {
        var agent = ctx.Agent;
        var type = JsonArgs.TryGetString(steering, "type", out var t) ? t : "Idle";
        switch (type)
        {
            case "Idle":
                break;

            case "Wander":
            {
                if (agent.IsMoving || ctx.Session.SimTimeSeconds < _nextWanderAt)
                {
                    break;
                }

                var radius = JsonArgs.GetDouble(steering, "radius", 4.0);
                var speed = JsonArgs.GetDouble(steering, "speed", agent.Profile.WalkSpeed);
                var interval = JsonArgs.GetDouble(steering, "interval", 1.5);
                var anchor = agent.Beliefs.GetPosition("spawn_position") ?? ctx.Entity.Position;
                var angle = ctx.Session.Rng.NextDouble() * Math.PI * 2;
                var distance = ctx.Session.Rng.NextDouble() * radius;
                var target = anchor + new Vector3(
                    (float)(Math.Cos(angle) * distance), 0, (float)(Math.Sin(angle) * distance));
                ctx.Ai.RequestPath(ctx, target, speed);
                _nextWanderAt = ctx.Session.SimTimeSeconds + interval;
                break;
            }

            case "FleeFrom":
            {
                if (agent.IsMoving)
                {
                    break; // finish the current flee leg before re-evaluating
                }

                var threat = agent.Beliefs.GetPosition("threat_position")
                             ?? agent.Beliefs.GetPosition("enemy_position")
                             ?? agent.Beliefs.GetPosition("last_enemy_position");
                if (threat is not { } from)
                {
                    break;
                }

                var speed = JsonArgs.GetDouble(steering, "speed", agent.Profile.RunSpeed);
                var distance = (float)JsonArgs.GetDouble(steering, "distance", 6.0);
                var away = ctx.Entity.Position - from;
                if (away.LengthSquared() < 0.0001f)
                {
                    away = Vector3.UnitX;
                }

                ctx.Ai.RequestPath(ctx, ctx.Entity.Position + Vector3.Normalize(away) * distance, speed);
                break;
            }

            case "MoveTo":
            {
                if (agent.IsMoving)
                {
                    break;
                }

                var target = MoveTargets.Resolve(ctx, steering, "target");
                if (target is { } destination)
                {
                    ctx.Ai.RequestPath(ctx, destination, JsonArgs.GetDouble(steering, "speed", agent.Profile.WalkSpeed));
                }

                break;
            }
        }
    }
}

/// <summary>Shared move-target resolution for tasks and steering: symbolic names over the agent's beliefs.</summary>
public static class MoveTargets
{
    /// <summary>
    /// Resolve a target spec found at <paramref name="property"/>:
    /// "patrol_point", "enemy", "threat", "sound", "scent", "last_enemy"
    /// (falls back to self), "spawn", or [x,y,z].
    /// </summary>
    public static Vector3? Resolve(AgentContext ctx, JsonElement args, string property)
        => args.ValueKind == JsonValueKind.Object && args.TryGetProperty(property, out var prop)
            ? ResolveSpec(ctx, prop)
            : null;

    /// <summary>Resolve a bare target spec element (symbol string or [x,y,z]).</summary>
    public static Vector3? ResolveSpec(AgentContext ctx, JsonElement spec)
    {
        if (spec.ValueKind == JsonValueKind.Array && spec.GetArrayLength() == 3)
        {
            var p = spec.EnumerateArray().Select(e => (float)e.GetDouble()).ToArray();
            return new Vector3(p[0], p[1], p[2]);
        }

        if (spec.ValueKind == JsonValueKind.String)
        {
            return ResolveSymbol(ctx, spec.GetString()!);
        }

        return null;
    }

    private static Vector3? ResolveSymbol(AgentContext ctx, string symbol)
    {
        var agent = ctx.Agent;
        return symbol switch
        {
            "patrol_point" when agent.Profile.PatrolPoints is { Count: > 0 } points =>
                ToVector(points[agent.PatrolIndex % points.Count]),
            "enemy" => agent.Beliefs.GetPosition("enemy_position") ?? agent.Beliefs.GetPosition("last_enemy_position"),
            "threat" => agent.Beliefs.GetPosition("threat_position")
                        ?? agent.Beliefs.GetPosition("enemy_position")
                        ?? agent.Beliefs.GetPosition("last_enemy_position"),
            "sound" => agent.Beliefs.GetPosition("sound_position"),
            "scent" => agent.Beliefs.GetPosition("scent_position"),
            "last_enemy" => agent.Beliefs.GetPosition("last_enemy_position") ?? ctx.Entity.Position,
            "spawn" => agent.Beliefs.GetPosition("spawn_position") ?? ctx.Entity.Position,
            _ => null,
        };
    }

    private static Vector3? ToVector(float[] coordinates)
        => coordinates.Length == 3 ? new Vector3(coordinates[0], coordinates[1], coordinates[2]) : null;
}
