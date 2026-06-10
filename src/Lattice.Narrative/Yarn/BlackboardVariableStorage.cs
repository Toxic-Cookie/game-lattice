using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Lattice.Core.Simulation;
using Yarn;

namespace Lattice.Narrative.Yarn;

/// <summary>
/// Yarn variable storage backed by the global blackboard: Yarn's
/// <c>$variables</c> ARE world state — no second source of truth (plan/03
/// §2). The leading <c>$</c> is stripped, so <c>$met_innkeeper</c> in Yarn
/// and <c>met_innkeeper</c> in quests/conditions are the same flag.
/// </summary>
public sealed class BlackboardVariableStorage : IVariableStorage
{
    private readonly Blackboard _blackboard;

    public BlackboardVariableStorage(Blackboard blackboard)
    {
        _blackboard = blackboard;
    }

    /// <inheritdoc/>
    public Program? Program { get; set; }

    /// <inheritdoc/>
    public ISmartVariableEvaluator? SmartVariableEvaluator { get; set; }

    public void SetValue(string variableName, string stringValue) => _blackboard.Write(ToKey(variableName), stringValue);

    public void SetValue(string variableName, float floatValue) => _blackboard.Write(ToKey(variableName), (double)floatValue);

    public void SetValue(string variableName, bool boolValue) => _blackboard.Write(ToKey(variableName), boolValue);

    /// <summary>Deliberately a no-op: dialogue must never wipe world state.</summary>
    public void Clear()
    {
    }

    public VariableKind GetVariableKind(string name)
    {
        if (_blackboard.HasKey(ToKey(name)))
        {
            return VariableKind.Stored;
        }

        return Program?.GetVariableKind(name) ?? VariableKind.Unknown;
    }

    public bool TryGetValue<T>(string variableName, [NotNullWhen(true)] out T? result)
    {
        if (GetVariableKind(variableName) == VariableKind.Smart && SmartVariableEvaluator is not null)
        {
            if (SmartVariableEvaluator.TryGetSmartVariable(variableName, out result) && result is not null)
            {
                return true;
            }

            result = default;
            return false;
        }

        if (_blackboard.TryRead(ToKey(variableName), out var stored) && TryConvert(stored, out result))
        {
            return true;
        }

        // fall back to the program's declared initial value
        if (Program is not null
            && Program.InitialValues.TryGetValue(variableName, out var operand)
            && TryConvert(FromOperand(operand), out result))
        {
            return result is not null;
        }

        result = default;
        return false;
    }

    private static string ToKey(string variableName) => variableName.TrimStart('$');

    private static object? FromOperand(Operand operand) => operand.ValueCase switch
    {
        Operand.ValueOneofCase.StringValue => operand.StringValue,
        Operand.ValueOneofCase.BoolValue => operand.BoolValue,
        Operand.ValueOneofCase.FloatValue => (double)operand.FloatValue,
        _ => null,
    };

    private static bool TryConvert<T>(object? value, [NotNullWhen(true)] out T? result)
    {
        switch (value)
        {
            case T direct:
                result = direct;
                return true;
            case IConvertible convertible:
                try
                {
                    result = (T)Convert.ChangeType(convertible, typeof(T), CultureInfo.InvariantCulture);
                    return true;
                }
                catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
                {
                    result = default;
                    return false;
                }

            default:
                result = default;
                return false;
        }
    }
}
