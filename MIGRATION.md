# Migrating from 0.x to 1.0

1.0 removes the Zenject and ExtDebugLogger dependencies; UniTask is the only one left.

| 0.x | 1.0 |
| --- | --- |
| Assembly `UniState` | `FirstSymbol.ExtState` (+ `FirstSymbol.ExtState.Editor`) |
| `BaseStateMachine` (abstract) | `StateMachine` (usable directly, implements `IStateMachine`, `IDisposable`) |
| `abstract Enum LogTag` | constructor `name` + `StateMachineLog` |
| `RegisterState<T>(state)` (protected) | `Register(state)` / `Register<T>()` (public) |
| `IState.Exit()` | `IState.Exit(CancellationToken)` |
| `IEnterableState.Enter()` | `IEnterState.Enter(CancellationToken)` (extends `IState`) |
| `IPayloadedState<T>.Enter(T)` | `IPayloadState<T>.Enter(T, CancellationToken)` (extends `IState`) |
| — | `State` / `PayloadState<T>` base classes, `IUpdatableState` |
| `Enter(int index)` on every machine | `SequentialStateMachine.Enter(index)` |
| `SequentialStateMachine.NextState()` | `Next()`, `Restart()`, `IsFinished`, `Completed` |
| `IStatesFactory` + Zenject `StatesFactory` | `IStateFactory`, `DelegateStateFactory`, `ActivatorStateFactory` |

## Behaviour changes

- Transitions are serialized; requesting one during another queues it instead of running both.
- The target state is resolved before the current state exits; unknown states throw `StateNotRegisteredException`.
- States are registered under their runtime type.
- `Enter(Type)` throws for payload-only states instead of silently skipping `Enter`.
- After the last state, `SequentialStateMachine.Next()` finishes the sequence instead of silently restarting.
