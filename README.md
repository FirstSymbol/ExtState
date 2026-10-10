# ExtState

Asynchronous state machine for Unity built on [UniTask](https://github.com/Cysharp/UniTask).

- **Serialized transitions** — a transition requested while another one runs (even from inside a state's
  `Enter`/`Exit`) is queued; states are never exited twice or entered concurrently.
- **Safe failures** — the target state is looked up *before* the current one exits; a failing `Exit` is logged and
  the transition continues; a failing `Enter` is reported to the caller and to `TransitionFailed`.
- **Payload states**, `IUpdatableState` ticking, re-enter policy, cancellation and `Dispose`.
- **DI-agnostic** — register instances, or let a factory create states (Zenject, VContainer or `Activator`).
- **Sequential machine** for linear flows (bootstrap, tutorials).
- **Debugger window** — `Window > ExtState > State Machine Debugger`: live machines, current state, history.

## Installation

1. Install [UniTask](https://github.com/Cysharp/UniTask#upm-package).
2. Package Manager → *Add package from git URL* → `https://github.com/FirstSymbol/ExtState.git`.

## States

```csharp
public sealed class LoadingState : State          // IEnterState with empty defaults
{
    private readonly IStateMachine _machine;
    public LoadingState(IStateMachine machine) => _machine = machine;

    public override async UniTask Enter(CancellationToken token)
    {
        await LoadEverything(token);
        _machine.Enter<MenuState>().Forget();     // queued, runs after this transition
    }
}

public sealed class LevelState : PayloadState<int>  // entered with data
{
    public override UniTask Enter(int level, CancellationToken token) => LoadLevel(level, token);
    public override UniTask Exit(CancellationToken token) => UnloadLevel(token);
}

public sealed class GameplayState : State, IUpdatableState
{
    public void Tick(float deltaTime) { /* called by machine.Tick */ }
}
```

Interfaces: `IState` (`Exit`), `IEnterState` (`Enter`), `IPayloadState<T>` (`Enter(T)`), `IUpdatableState` (`Tick`).

## Machine

```csharp
var machine = new StateMachine("Game");
machine.Register(new LoadingState(machine));
machine.Register<MenuState>();                     // created by the factory
machine.Register<LevelState>();

await machine.Enter<LoadingState>();
await machine.Enter<LevelState, int>(3);
await machine.Exit();                              // leave the current state

machine.IsIn<MenuState>();
machine.CurrentState; machine.PreviousState; machine.IsTransitioning;
machine.StateChanged += (from, to) => Debug.Log($"{from} -> {to}");
machine.ReenterPolicy = ReenterPolicy.Reenter;     // default: Ignore
machine.Tick(Time.deltaTime);                      // forwards to IUpdatableState
machine.Dispose();                                 // exits the state, cancels pending transitions
```

> Do not `await` a transition from inside `Enter`/`Exit` of a state of the same machine: it is queued behind the
> running transition, so awaiting it there would wait for itself. Use `.Forget()`.

Entering an unregistered state throws `StateNotRegisteredException` before anything changes.
`Enter(Type)` works for `IEnterState` states; payload states need `Enter<TState, TPayload>()`.

### Factories

```csharp
// Default: Activator, passing the machine to a constructor that takes it.
new StateMachine("Game");

// Zenject
new StateMachine("Game", new DelegateStateFactory((type, machine) =>
    (IState)container.Instantiate(type, new object[] { machine })));

// VContainer (states registered in the container)
new StateMachine("Game", new DelegateStateFactory(type => resolver.Resolve(type)));
```

### Sequential machine

```csharp
var bootstrap = new SequentialStateMachine("Bootstrap");
bootstrap.Register<InitServicesState>();
bootstrap.Register<LoadConfigState>();
bootstrap.Register<LoginState>();
bootstrap.Completed += () => Debug.Log("Bootstrap finished");

await bootstrap.Next();   // InitServices
await bootstrap.Next();   // LoadConfig
await bootstrap.Next();   // Login
await bootstrap.Next();   // exits Login, IsFinished = true, Completed raised
await bootstrap.Restart();
```

## Logging

```csharp
StateMachineLog.Level = StateMachineLogLevel.Info;   // logs every transition
StateMachineLog.Handler = (type, message) => MyLogger.Log(message);
```

## Upgrading from 0.x

See [MIGRATION.md](MIGRATION.md).
