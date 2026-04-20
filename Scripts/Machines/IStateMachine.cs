using System;
using Cysharp.Threading.Tasks;

namespace ExtState
{
  public interface IStateMachine
  {
    Enum LogTag { get; }
    public UniTask Enter<TState>()
      where TState : class, IState, IEnterableState;
    public UniTask Enter<TState, TPayload>(TPayload payload)
      where TState : class, IState, IPayloadedState<TPayload>;
    public UniTask Enter(Type type);
    public UniTask Enter(int index);
  }
}