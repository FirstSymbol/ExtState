using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace ExtState
{
  /// <summary>
  /// Runs registered states in registration order. <see cref="Next"/> after the last state exits it,
  /// marks the machine finished and raises <see cref="Completed"/>; it does not start over by itself.
  /// </summary>
  public class SequentialStateMachine : StateMachine
  {
    public SequentialStateMachine(string name = null, IStateFactory factory = null) : base(name, factory)
    {
    }

    public bool IsFinished { get; private set; }

    /// <summary>Index of the current state in registration order, or -1.</summary>
    public int CurrentIndex => CurrentState == null ? -1 : IndexOf(CurrentState);

    public event Action Completed;

    /// <summary>Enters the next state (the first one when none is active).</summary>
    public UniTask Next(CancellationToken cancellationToken = default)
    {
      return Enqueue(ResolveNext, EnterWithoutPayload, cancellationToken, state =>
      {
        if (state != null || IsFinished)
          return;

        IsFinished = true;
        Completed?.Invoke();
      });
    }

    /// <summary>Starts the sequence from the first state again.</summary>
    public UniTask Restart(CancellationToken cancellationToken = default)
    {
      IsFinished = false;
      return Enter(0, cancellationToken);
    }

    public UniTask Enter(int index, CancellationToken cancellationToken = default)
    {
      if (index < 0 || index >= States.Count)
        throw new ArgumentOutOfRangeException(nameof(index), $"{Name} has {States.Count} states.");

      IState state = States[index];
      return Enqueue(() => state, EnterWithoutPayload, cancellationToken, _ => IsFinished = false);
    }

    private IState ResolveNext()
    {
      if (IsFinished)
      {
        StateMachineLog.Warning($"[{Name}] The sequence is finished; call Restart() to run it again.");
        return null;
      }

      int next = CurrentState == null ? 0 : IndexOf(CurrentState) + 1;
      return next < States.Count ? States[next] : null;
    }
  }
}
