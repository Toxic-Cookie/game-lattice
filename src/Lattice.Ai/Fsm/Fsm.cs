namespace Lattice.Ai.Fsm;

/// <summary>A state in a delegate-based FSM: per-tick logic plus optional lifecycle hooks (research ch02 §2.2).</summary>
public sealed class FsmState<TOwner>
{
    public FsmState(string name, Action<TOwner> tick, Action<TOwner>? onEnter = null, Action<TOwner>? onExit = null)
    {
        Name = name;
        Tick = tick;
        OnEnter = onEnter;
        OnExit = onExit;
    }

    public string Name { get; }

    public Action<TOwner> Tick { get; }

    public Action<TOwner>? OnEnter { get; }

    public Action<TOwner>? OnExit { get; }
}

/// <summary>
/// Minimal generic FSM: holds the active state and runs it. All transition
/// logic lives inside state functions (research ch02 — the FSM class stays
/// generic and reusable).
/// </summary>
public sealed class Fsm<TOwner>
{
    private readonly TOwner _owner;

    public Fsm(TOwner owner)
    {
        _owner = owner;
    }

    public FsmState<TOwner>? Current { get; private set; }

    public void SetState(FsmState<TOwner>? state)
    {
        if (ReferenceEquals(state, Current))
        {
            return;
        }

        Current?.OnExit?.Invoke(_owner);
        Current = state;
        Current?.OnEnter?.Invoke(_owner);
    }

    public void Update() => Current?.Tick(_owner);
}

/// <summary>
/// Stack FSM: push/pop gives interrupted states automatic resume — the
/// machine's history is the context (research ch02 §2.3, FSM theory case
/// study Part 5). Pushing the state already on top is a guarded no-op.
/// </summary>
public sealed class StackFsm<TOwner>
{
    private readonly List<FsmState<TOwner>> _stack = [];
    private readonly TOwner _owner;

    public StackFsm(TOwner owner)
    {
        _owner = owner;
    }

    public FsmState<TOwner>? Current => _stack.Count > 0 ? _stack[^1] : null;

    public int Depth => _stack.Count;

    public IEnumerable<string> StackNames => _stack.Select(s => s.Name);

    public void Push(FsmState<TOwner> state)
    {
        if (ReferenceEquals(Current, state))
        {
            return; // re-push guard: a condition firing every frame must not grow the stack
        }

        _stack.Add(state);
        state.OnEnter?.Invoke(_owner);
    }

    public void Pop()
    {
        if (_stack.Count == 0)
        {
            return;
        }

        _stack[^1].OnExit?.Invoke(_owner);
        _stack.RemoveAt(_stack.Count - 1);
    }

    public void Replace(FsmState<TOwner> state)
    {
        Pop();
        Push(state);
    }

    public void Update() => Current?.Tick(_owner);
}
