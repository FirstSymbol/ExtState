# Changelog

## [1.0.0]

See [MIGRATION.md](MIGRATION.md).

### Added
- Serialized transition queue (safe to request transitions from inside states).
- `CurrentState`, `PreviousState`, `IsTransitioning`, `StateChanged`, `TransitionFailed`, transition history.
- `ReenterPolicy`, cancellation tokens, `Dispose`, `IUpdatableState` and `Tick`.
- `State` / `PayloadState<T>` base classes.
- DI-agnostic factories: `DelegateStateFactory`, `ActivatorStateFactory`.
- `SequentialStateMachine.Restart`, `IsFinished`, `Completed`.
- `StateMachineLog` with levels and a pluggable handler.
- `Window > ExtState > State Machine Debugger`.
- EditMode tests.

### Fixed
- Concurrent `Enter` calls exiting a state twice and racing for the active state.
- Exceptions leaving the machine half-way through a transition.
- Payload states activated through `Enter(Type)`/`Enter(int)` without being entered.
- `BaseStateMachine` not implementing `IStateMachine`.
- Sequential machine silently restarting after the last state.

### Removed
- Zenject and ExtDebugLogger dependencies, `LogTag`.
