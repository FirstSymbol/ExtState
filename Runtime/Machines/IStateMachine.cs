using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace ExtState
{
  public interface IStateMachine
  {
    string Name { get; }
    IState CurrentState { get; }
    IState PreviousState { get; }
    bool IsTransitioning { get; }
    IReadOnlyList<IState> States { get; }

    /// <summary>Raised after a transition completed: (from, to). Either can be null.</summary>
    event Action<IState, IState> StateChanged;

    UniTask Enter<TState>(CancellationToken cancellationToken = default) where TState : class, IEnterState;

    UniTask Enter<TState, TPayload>(TPayload payload, CancellationToken cancellationToken = default)
      where TState : class, IPayloadState<TPayload>;

    UniTask Enter(Type stateType, CancellationToken cancellationToken = default);

    /// <summary>Exits the current state and leaves the machine without a state.</summary>
    UniTask Exit(CancellationToken cancellationToken = default);

    bool IsIn<TState>() where TState : class, IState;
  }
}
