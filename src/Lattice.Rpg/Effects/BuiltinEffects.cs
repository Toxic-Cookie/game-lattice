using System.Numerics;
using System.Text.Json;
using Lattice.Core.Content;
using Lattice.Core.Events;
using Lattice.Rpg.Defs;

namespace Lattice.Rpg.Effects;

/// <summary>The built-in effect vocabulary (plan/02 §3). Conventions: "stat" properties take stat def IDs; formula strings use stat keys.</summary>
public static class BuiltinEffects
{
    public static EffectRegistry CreateDefault()
    {
        var registry = new EffectRegistry();
        registry.Register(new DealDamageEffect());
        registry.Register(new HealEffect());
        registry.Register(new ModifyStatEffect());
        registry.Register(new ApplyStatusEffect());
        registry.Register(new RemoveStatusEffect());
        registry.Register(new GiveItemEffect());
        registry.Register(new RemoveItemEffect());
        registry.Register(new SetFlagEffect());
        registry.Register(new PublishEventEffect());
        registry.Register(new SpawnEntityEffect());
        registry.Register(new TeleportEffect());
        registry.Register(new AreaDamageEffect());
        return registry;
    }

    /// <summary>Resolve a "stat" arg (stat def ID, default stat_hp) to its formula key.</summary>
    internal static string ResolveStatKey(EffectContext ctx, JsonElement args)
    {
        var statId = JsonArgs.TryGetString(args, "stat", out var s) ? s : "stat_hp";
        return ctx.Rpg.Stats.TryGetById(statId, out var def) ? def.Key : statId;
    }

    internal static void ValidateStatRef(JsonElement args, EffectValidationContext v)
    {
        if (JsonArgs.TryGetString(args, "stat", out var statId) && !v.Registry.TryGet<StatDef>(statId, out _))
        {
            v.Error($"'stat' references missing StatDef '{statId}'.");
        }
    }
}

internal sealed class DealDamageEffect : IEffectExecutor
{
    public string Type => "DealDamage";

    public void Execute(EffectContext ctx, JsonElement args)
    {
        if (ctx.Target is null || ctx.Rpg.GetSheet(ctx.Target) is not { } sheet)
        {
            return;
        }

        var amount = ctx.Session.Formulas.Evaluate(JsonArgs.GetFormula(args, "formula"), ctx.Scope);
        var key = BuiltinEffects.ResolveStatKey(ctx, args);
        sheet.ModifyBase(key, -amount, ctx.Source);
        ctx.Session.Events.Publish("Entity.Damaged", EventPayload.Of(
            ("instanceId", ctx.Target.InstanceId),
            ("stat", key),
            ("amount", amount),
            ("sourceId", ctx.Source?.InstanceId)));
    }

    public void Validate(JsonElement args, EffectValidationContext v)
    {
        v.RequireFormula(args, "formula");
        BuiltinEffects.ValidateStatRef(args, v);
    }
}

internal sealed class HealEffect : IEffectExecutor
{
    public string Type => "Heal";

    public void Execute(EffectContext ctx, JsonElement args)
    {
        if (ctx.Target is null || ctx.Rpg.GetSheet(ctx.Target) is not { } sheet)
        {
            return;
        }

        var amount = ctx.Session.Formulas.Evaluate(JsonArgs.GetFormula(args, "formula"), ctx.Scope);
        sheet.ModifyBase(BuiltinEffects.ResolveStatKey(ctx, args), amount, ctx.Source);
    }

    public void Validate(JsonElement args, EffectValidationContext v)
    {
        v.RequireFormula(args, "formula");
        BuiltinEffects.ValidateStatRef(args, v);
    }
}

internal sealed class ModifyStatEffect : IEffectExecutor
{
    public string Type => "ModifyStat";

    public void Execute(EffectContext ctx, JsonElement args)
    {
        if (ctx.Target is null || ctx.Rpg.GetSheet(ctx.Target) is not { } sheet)
        {
            return;
        }

        var delta = ctx.Session.Formulas.Evaluate(JsonArgs.GetFormula(args, "formula"), ctx.Scope);
        sheet.ModifyBase(BuiltinEffects.ResolveStatKey(ctx, args), delta, ctx.Source);
    }

    public void Validate(JsonElement args, EffectValidationContext v)
    {
        v.RequireFormula(args, "formula");
        BuiltinEffects.ValidateStatRef(args, v);
    }
}

internal sealed class ApplyStatusEffect : IEffectExecutor
{
    public string Type => "ApplyStatus";

    public void Execute(EffectContext ctx, JsonElement args)
    {
        if (ctx.Target is null)
        {
            return;
        }

        var statusId = JsonArgs.GetString(args, "status");
        if (ctx.Session.Defs.TryGet<StatusEffectDef>(statusId, out var def))
        {
            ctx.Rpg.GetStatusEffects(ctx.Target)?.Apply(def, ctx.Source);
        }
    }

    public void Validate(JsonElement args, EffectValidationContext v) => v.RequireDef<StatusEffectDef>(args, "status");
}

internal sealed class RemoveStatusEffect : IEffectExecutor
{
    public string Type => "RemoveStatus";

    public void Execute(EffectContext ctx, JsonElement args)
    {
        if (ctx.Target is null)
        {
            return;
        }

        ctx.Rpg.GetStatusEffects(ctx.Target)?.Remove(JsonArgs.GetString(args, "status"));
    }

    public void Validate(JsonElement args, EffectValidationContext v) => v.RequireDef<StatusEffectDef>(args, "status");
}

internal sealed class GiveItemEffect : IEffectExecutor
{
    public string Type => "GiveItem";

    public void Execute(EffectContext ctx, JsonElement args)
    {
        if (ctx.Target is null)
        {
            return;
        }

        var amount = (int)Math.Round(ctx.Session.Formulas.Evaluate(JsonArgs.GetFormula(args, "amount", "1"), ctx.Scope));
        ctx.Rpg.GiveItem(ctx.Target, JsonArgs.GetString(args, "item"), amount);
    }

    public void Validate(JsonElement args, EffectValidationContext v)
    {
        v.RequireDef<ItemDef>(args, "item");
        v.RequireFormula(args, "amount", required: false);
    }
}

internal sealed class RemoveItemEffect : IEffectExecutor
{
    public string Type => "RemoveItem";

    public void Execute(EffectContext ctx, JsonElement args)
    {
        if (ctx.Target is null)
        {
            return;
        }

        var amount = (int)Math.Round(ctx.Session.Formulas.Evaluate(JsonArgs.GetFormula(args, "amount", "1"), ctx.Scope));
        ctx.Rpg.RemoveItem(ctx.Target, JsonArgs.GetString(args, "item"), amount);
    }

    public void Validate(JsonElement args, EffectValidationContext v)
    {
        v.RequireDef<ItemDef>(args, "item");
        v.RequireFormula(args, "amount", required: false);
    }
}

internal sealed class SetFlagEffect : IEffectExecutor
{
    public string Type => "SetFlag";

    public void Execute(EffectContext ctx, JsonElement args)
    {
        var flag = JsonArgs.GetString(args, "flag");
        if (args.TryGetProperty("value", out var valueProp) && JsonValueHelper.TryToPlain(valueProp, out var value))
        {
            ctx.Session.Flags.Write(flag, value);
        }
    }

    public void Validate(JsonElement args, EffectValidationContext v)
    {
        if (!JsonArgs.TryGetString(args, "flag", out _))
        {
            v.Error("missing required string property 'flag'.");
        }

        if (!args.TryGetProperty("value", out var valueProp) || !JsonValueHelper.TryToPlain(valueProp, out _))
        {
            v.Error("'value' must be a bool, number, or string.");
        }
    }
}

internal sealed class PublishEventEffect : IEffectExecutor
{
    public string Type => "PublishEvent";

    public void Execute(EffectContext ctx, JsonElement args)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["sourceId"] = ctx.Source?.InstanceId,
            ["targetId"] = ctx.Target?.InstanceId,
        };
        if (args.TryGetProperty("payload", out var payloadProp) && payloadProp.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in payloadProp.EnumerateObject())
            {
                payload[prop.Name] = JsonValueHelper.TryToPlain(prop.Value, out var value) ? value : null;
            }
        }

        ctx.Session.Events.Publish(JsonArgs.GetString(args, "event"), payload, ctx.Session.Tick);
    }

    public void Validate(JsonElement args, EffectValidationContext v)
    {
        if (!JsonArgs.TryGetString(args, "event", out _))
        {
            v.Error("missing required string property 'event'.");
        }
    }
}

internal sealed class SpawnEntityEffect : IEffectExecutor
{
    public string Type => "SpawnEntity";

    public void Execute(EffectContext ctx, JsonElement args)
    {
        var position = ctx.Target?.Position ?? ctx.Source?.Position ?? default;
        if (args.TryGetProperty("position", out var posProp) && posProp.ValueKind == JsonValueKind.Array && posProp.GetArrayLength() == 3)
        {
            var p = posProp.EnumerateArray().Select(e => (float)e.GetDouble()).ToArray();
            position = new Vector3(p[0], p[1], p[2]);
        }

        ctx.Session.World.Spawn(JsonArgs.GetString(args, "entity"), position);
    }

    public void Validate(JsonElement args, EffectValidationContext v) => v.RequireDef<EntityTemplateDef>(args, "entity");
}

internal sealed class TeleportEffect : IEffectExecutor
{
    public string Type => "Teleport";

    public void Execute(EffectContext ctx, JsonElement args)
    {
        if (ctx.Target is null
            || !args.TryGetProperty("position", out var posProp)
            || posProp.ValueKind != JsonValueKind.Array
            || posProp.GetArrayLength() != 3)
        {
            return;
        }

        var p = posProp.EnumerateArray().Select(e => (float)e.GetDouble()).ToArray();
        ctx.Target.Position = new Vector3(p[0], p[1], p[2]);
    }

    public void Validate(JsonElement args, EffectValidationContext v)
    {
        if (!args.TryGetProperty("position", out var posProp) || posProp.ValueKind != JsonValueKind.Array || posProp.GetArrayLength() != 3)
        {
            v.Error("'position' must be a [x, y, z] array.");
        }
    }
}

internal sealed class AreaDamageEffect : IEffectExecutor
{
    public string Type => "AreaDamage";

    public void Execute(EffectContext ctx, JsonElement args)
    {
        var center = ctx.Target?.Position ?? ctx.Source?.Position ?? default;
        var radius = JsonArgs.GetDouble(args, "radius", 5);
        var radiusSq = radius * radius;
        var amount = ctx.Session.Formulas.Evaluate(JsonArgs.GetFormula(args, "formula"), ctx.Scope);
        var key = BuiltinEffects.ResolveStatKey(ctx, args);

        foreach (var entity in ctx.Session.World.All.ToList())
        {
            if (entity == ctx.Source || Vector3.DistanceSquared(entity.Position, center) > radiusSq)
            {
                continue;
            }

            ctx.Rpg.GetSheet(entity)?.ModifyBase(key, -amount, ctx.Source);
            ctx.Session.Events.Publish("Entity.Damaged", EventPayload.Of(
                ("instanceId", entity.InstanceId),
                ("stat", key),
                ("amount", amount),
                ("sourceId", ctx.Source?.InstanceId)));
        }
    }

    public void Validate(JsonElement args, EffectValidationContext v)
    {
        v.RequireFormula(args, "formula");
        BuiltinEffects.ValidateStatRef(args, v);
    }
}
