using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ExtState
{
  public enum ReenterPolicy
  {
    /// <summary>Entering the current state again does nothing.</summary>
    Ignore,

    /// <summary>Entering the current state again exits and enters it.</summary>
    Reenter
  }

  /// <summary>A completed transition, kept for diagnostics.</summary>
  public readonly struct StateTransitionRecord
  {
    public readonly string From;
    public readonly string To;
    public readonly double StartTime;
    public readonly double Duration;
    public readonly string Error;

    public StateTransitionRecord(string from, string to, double startTime, double duration, string error)
    {
      From = from;
      To = to;
      StartTime = startTime;
      Duration = duration;
      Error = error;
    }
  }

  public sealed class StateNotRegisteredException : InvalidOperationException
  {
    public StateNotRegisteredException(string machine, Type stateType)
      : base($"{machine}: state '{stateType?.Name}' is not registered.")
    {
      StateType = stateType;
    }

    public Type StateType { get; }
  }

  /// <summary>
  /// Asynchronous state machine. Transitions are serialized: a transition requested while another one runs
  /// (including from inside a state's Enter/Exit) is queued and executed afterwards.
  /// </summary>
  /// <remarks>
  /// Do not <c>await</c> a transition from inside a state's Enter/Exit — it is queued behind the running one;
  /// start it with <c>.Forget()</c> instead.
  /// </remarks>
  public class StateMachine : IStateMachine, IDisposable
  {
    private sealed class Request
    {
      public Func<IState> Resolve;
      public Func<IState, CancellationToken, UniTask> Enter;
      public Action<IState> Applied;
      public CancellationToken Token;
      public readonly UniTaskCompletionSource Completion = new();
    }

    private readonly Dictionary<Type, IState> _states = new();
    private readonly List<IState> _order = new();
    private readonly Queue<Request> _queue = new();
    private readonly List<StateTransitionRecord> _history = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly IStateFactory _factory;
    private bool _processing;
    private bool _disposed;

    public StateMachine(string name = null, IStateFactory factory = null)
    {
      Name = string.IsNullOrEmpty(name) ? GetType().Name : name;
      _factory = factory ?? ActivatorStateFactory.Instance;
      StateMachineRegistry.Register(this);
    }

    public string Name { get; }
    public IState CurrentState { get; private set; }
    public IState PreviousState { get; private set; }
    public bool IsTransitioning => _processing;
    public bool IsDisposed => _disposed;
    public ReenterPolicy ReenterPolicy { get; set; } = ReenterPolicy.Ignore;
    public IReadOnlyList<IState> States => _order;

    /// <summary>Most recent transitions (oldest first).</summary>
    public IReadOnlyList<StateTransitionRecord> History => _history;

    /// <summary>Number of transitions kept in <see cref="History"/>. 0 disables the history.</summary>
    public int HistoryCapacity { get; set; } = 32;

    /// <summary>Cancelled when the machine is disposed.</summary>
    public CancellationToken LifetimeToken => _lifetime.Token;

    public event Action<IState, IState> StateChanged;

    /// <summary>Raised when a state's Enter throws. The exception is also passed to the awaiting caller.</summary>
    public event Action<Exception> TransitionFailed;

    #region Registration

    /// <summary>Registers a state instance under its runtime type.</summary>
    public TState Register<TState>(TState state) where TState : class, IState
    {
      ThrowIfDisposed();
      if (state == null)
        throw new ArgumentNullException(nameof(state));

      Type type = state.GetType();
      if (_states.ContainsKey(type))
        throw new InvalidOperationException($"{Name}: a state of type '{type.Name}' is already registered.");

      _states.Add(type, state);
      _order.Add(state);
      return state;
    }

    /// <summary>Creates the state with the machine's <see cref="IStateFactory"/> and registers it.</summary>
    public TState Register<TState>() where TState : class, IState =>
      Register((TState)_factory.Create(typeof(TState), this));

    public bool IsRegistered<TState>() where TState : class, IState => _states.ContainsKey(typeof(TState));

    public bool IsRegistered(Type stateType) => stateType != null && _states.ContainsKey(stateType);

    public TState Get<TState>() where TState : class, IState => (TState)GetState(typeof(TState));

    public bool TryGet<TState>(out TState state) where TState : class, IState
    {
      state = _states.TryGetValue(typeof(TState), out IState value) ? (TState)value : null;
      return state != null;
    }

    public bool IsIn<TState>() where TState : class, IState => CurrentState is TState;

    #endregion

    #region Transitions

    public UniTask Enter<TState>(CancellationToken cancellationToken = default) where TState : class, IEnterState
    {
      IState state = GetState(typeof(TState));
      return Enqueue(() => state, EnterWithoutPayload, cancellationToken);
    }

    public UniTask Enter<TState, TPayload>(TPayload payload, CancellationToken cancellationToken = default)
      where TState : class, IPayloadState<TPayload>
    {
      IState state = GetState(typeof(TState));
      return Enqueue(() => state, (s, token) => ((IPayloadState<TPayload>)s).Enter(payload, token), cancellationToken);
    }

    public UniTask Enter(Type stateType, CancellationToken cancellationToken = default)
    {
      IState state = GetState(stateType);
      if (state is not IEnterState)
        throw new InvalidOperationException($"{Name}: '{stateType.Name}' needs a payload, use Enter<TState, TPayload>().");
      return Enqueue(() => state, EnterWithoutPayload, cancellationToken);
    }

    public UniTask Exit(CancellationToken cancellationToken = default) =>
      Enqueue(() => null, null, cancellationToken);

    /// <summary>Forwards a frame update to the current <see cref="IUpdatableState"/> (not during transitions).</summary>
    public void Tick(float deltaTime)
    {
      if (!_processing && CurrentState is IUpdatableState updatable)
        updatable.Tick(deltaTime);
    }

    #endregion

    /// <summary>Exits the current state and cancels pending and running transitions.</summary>
    public void Dispose()
    {
      if (_disposed)
        return;

      _disposed = true;
      _lifetime.Cancel();
      while (_queue.Count > 0)
        _queue.Dequeue().Completion.TrySetCanceled();

      IState current = CurrentState;
      CurrentState = null;

      // A running transition exits the state it is entering by itself.
      if (current != null && !_processing)
        SafeExit(current).Forget();

      StateMachineRegistry.Unregister(this);
    }

    protected IState GetState(Type stateType)
    {
      if (stateType == null || !_states.TryGetValue(stateType, out IState state))
        throw new StateNotRegisteredException(Name, stateType);
      return state;
    }

    protected int IndexOf(IState state) => _order.IndexOf(state);

    /// <summary>
    /// Queues a transition. <paramref name="resolve"/> runs when the transition starts and returns the target
    /// (null = exit). <paramref name="applied"/> runs after a successful transition.
    /// </summary>
    protected UniTask Enqueue(Func<IState> resolve, Func<IState, CancellationToken, UniTask> enter,
      CancellationToken cancellationToken, Action<IState> applied = null)
    {
      ThrowIfDisposed();
      var request = new Request { Resolve = resolve, Enter = enter, Applied = applied, Token = cancellationToken };
      _queue.Enqueue(request);
      if (!_processing)
        ProcessQueue().Forget();
      return request.Completion.Task;
    }

    protected static UniTask EnterWithoutPayload(IState state, CancellationToken token) =>
      state is IEnterState enter
        ? enter.Enter(token)
        : throw new InvalidOperationException($"'{state.GetType().Name}' needs a payload and cannot be entered without one.");

    private async UniTaskVoid ProcessQueue()
    {
      _processing = true;
      try
      {
        while (_queue.Count > 0 && !_disposed)
          await Execute(_queue.Dequeue());
      }
      finally
      {
        _processing = false;
      }
    }

    private async UniTask Execute(Request request)
    {
      if (_disposed || request.Token.IsCancellationRequested)
      {
        request.Completion.TrySetCanceled();
        return;
      }

      IState target;
      try
      {
        target = request.Resolve();
      }
      catch (Exception exception)
      {
        request.Completion.TrySetException(exception);
        return;
      }

      IState from = CurrentState;
      if ((target == null && from == null) ||
          (target != null && ReferenceEquals(target, from) && ReenterPolicy == ReenterPolicy.Ignore))
      {
        request.Completion.TrySetResult();
        return;
      }

      using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, request.Token);
      CancellationToken token = linked.Token;
      double start = Time.realtimeSinceStartupAsDouble;
      string exitError = null;

      if (from != null)
        exitError = await SafeExit(from, token);

      if (_disposed)
      {
        request.Completion.TrySetCanceled();
        return;
      }

      PreviousState = from;
      CurrentState = target;

      Exception enterError = null;
      if (target != null && request.Enter != null && !token.IsCancellationRequested)
      {
        try
        {
          await request.Enter(target, token);
        }
        catch (OperationCanceledException)
        {
          // Reported through the token below.
        }
        catch (Exception exception)
        {
          enterError = exception;
        }
      }

      if (_disposed)
      {
        // Disposed while entering: leave the state that was just entered.
        if (target != null)
          SafeExit(target).Forget();
        request.Completion.TrySetCanceled();
        return;
      }

      Record(from, target, start, enterError?.Message ?? exitError);
      StateChanged?.Invoke(from, target);

      if (enterError != null)
      {
        TransitionFailed?.Invoke(enterError);
        request.Completion.TrySetException(enterError);
      }
      else if (token.IsCancellationRequested)
      {
        request.Completion.TrySetCanceled();
      }
      else
      {
        if (StateMachineLog.IsEnabled(StateMachineLogLevel.Info))
          StateMachineLog.Info($"[{Name}] {NameOf(from)} → {NameOf(target)}");
        request.Applied?.Invoke(target);
        request.Completion.TrySetResult();
      }
    }

    /// <returns>Error message when Exit threw, otherwise null.</returns>
    private async UniTask<string> SafeExit(IState state, CancellationToken token = default)
    {
      try
      {
        await state.Exit(token);
        return null;
      }
      catch (OperationCanceledException)
      {
        return null;
      }
      catch (Exception exception)
      {
        // Exit failures must not block the machine: log and continue with the transition.
        StateMachineLog.Error($"[{Name}] Exit of '{NameOf(state)}' failed: {exception}");
        return $"Exit failed: {exception.Message}";
      }
    }

    private void Record(IState from, IState to, double start, string error)
    {
      if (HistoryCapacity <= 0)
        return;

      if (_history.Count >= HistoryCapacity)
        _history.RemoveRange(0, _history.Count - HistoryCapacity + 1);
      _history.Add(new StateTransitionRecord(NameOf(from), NameOf(to), start,
        Time.realtimeSinceStartupAsDouble - start, error));
    }

    private static string NameOf(IState state) => state == null ? "∅" : state.GetType().Name;

    private void ThrowIfDisposed()
    {
      if (_disposed)
        throw new ObjectDisposedException(Name);
    }
  }
}
