using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ExtState.Tests
{
  public sealed class StateMachineTests
  {
    private static readonly List<string> Log = new();

    private abstract class LoggingState : IEnterState
    {
      public UniTaskCompletionSource EnterGate;

      public virtual async UniTask Enter(CancellationToken cancellationToken)
      {
        Log.Add($"enter {GetType().Name}");
        if (EnterGate != null)
          await EnterGate.Task;
      }

      public virtual UniTask Exit(CancellationToken cancellationToken)
      {
        Log.Add($"exit {GetType().Name}");
        return UniTask.CompletedTask;
      }
    }

    private sealed class A : LoggingState { }
    private sealed class B : LoggingState { }
    private sealed class C : LoggingState { }

    private sealed class Chain : LoggingState
    {
      public StateMachine Machine;

      public override async UniTask Enter(CancellationToken cancellationToken)
      {
        await base.Enter(cancellationToken);
        Machine.Enter<B>().Forget();
        Log.Add("chain requested B");
      }
    }

    private sealed class BrokenExit : LoggingState
    {
      public override UniTask Exit(CancellationToken cancellationToken) => throw new InvalidOperationException("exit failed");
    }

    private sealed class BrokenEnter : LoggingState
    {
      public override UniTask Enter(CancellationToken cancellationToken) => throw new InvalidOperationException("enter failed");
    }

    private sealed class Payload : PayloadState<int>
    {
      public int Received;

      public override UniTask Enter(int payload, CancellationToken cancellationToken)
      {
        Received = payload;
        return UniTask.CompletedTask;
      }
    }

    private sealed class WithMachine : State
    {
      public WithMachine(IStateMachine machine) => Machine = machine;
      public IStateMachine Machine { get; }
    }

    private StateMachine _machine;

    [SetUp]
    public void SetUp()
    {
      Log.Clear();
      _machine = new StateMachine("Test");
    }

    [TearDown]
    public void TearDown() => _machine.Dispose();

    [Test]
    public void EnterExitsPreviousState()
    {
      _machine.Register(new A());
      _machine.Register(new B());

      _machine.Enter<A>().Forget();
      _machine.Enter<B>().Forget();

      CollectionAssert.AreEqual(new[] { "enter A", "exit A", "enter B" }, Log);
      Assert.IsTrue(_machine.IsIn<B>());
      Assert.IsInstanceOf<A>(_machine.PreviousState);
    }

    [Test]
    public void PayloadIsPassed()
    {
      Payload state = _machine.Register(new Payload());
      _machine.Enter<Payload, int>(42).Forget();
      Assert.AreEqual(42, state.Received);
    }

    [Test]
    public void ConcurrentTransitionsAreSerialized()
    {
      var a = _machine.Register(new A { EnterGate = new UniTaskCompletionSource() });
      _machine.Register(new B());
      _machine.Register(new C());

      _machine.Enter<A>().Forget();
      _machine.Enter<B>().Forget();
      _machine.Enter<C>().Forget();
      Assert.IsTrue(_machine.IsTransitioning);
      CollectionAssert.AreEqual(new[] { "enter A" }, Log);

      a.EnterGate.TrySetResult();

      CollectionAssert.AreEqual(new[] { "enter A", "exit A", "enter B", "exit B", "enter C" }, Log);
      Assert.IsTrue(_machine.IsIn<C>());
      Assert.IsFalse(_machine.IsTransitioning);
    }

    [Test]
    public void TransitionRequestedFromEnterIsQueued()
    {
      _machine.Register(new Chain { Machine = _machine });
      _machine.Register(new B());

      _machine.Enter<Chain>().Forget();

      CollectionAssert.AreEqual(new[] { "enter Chain", "chain requested B", "exit Chain", "enter B" }, Log);
      Assert.IsTrue(_machine.IsIn<B>());
    }

    [Test]
    public void UnknownStateThrowsBeforeLeavingCurrent()
    {
      _machine.Register(new A());
      _machine.Enter<A>().Forget();

      Assert.Throws<StateNotRegisteredException>(() => _machine.Enter<B>());
      Assert.IsTrue(_machine.IsIn<A>());
      CollectionAssert.AreEqual(new[] { "enter A" }, Log);
    }

    [Test]
    public void ExitFailureDoesNotBlockTransition()
    {
      _machine.Register(new BrokenExit());
      _machine.Register(new B());
      _machine.Enter<BrokenExit>().Forget();

      LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Exit of 'BrokenExit' failed"));
      _machine.Enter<B>().Forget();

      Assert.IsTrue(_machine.IsIn<B>());
      StringAssert.Contains("Exit failed", _machine.History[_machine.History.Count - 1].Error);
    }

    [Test]
    public void EnterFailureIsReportedToCaller()
    {
      _machine.Register(new BrokenEnter());
      Exception reported = null;
      _machine.TransitionFailed += e => reported = e;

      UniTask task = _machine.Enter<BrokenEnter>();

      Assert.AreEqual(UniTaskStatus.Faulted, task.Status);
      Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());
      Assert.IsNotNull(reported);
      Assert.IsTrue(_machine.IsIn<BrokenEnter>(), "The machine stays in the state whose Enter failed.");
    }

    [Test]
    public void ReenterPolicy()
    {
      _machine.Register(new A());
      _machine.Enter<A>().Forget();
      _machine.Enter<A>().Forget();
      CollectionAssert.AreEqual(new[] { "enter A" }, Log);

      _machine.ReenterPolicy = ExtState.ReenterPolicy.Reenter;
      _machine.Enter<A>().Forget();
      CollectionAssert.AreEqual(new[] { "enter A", "exit A", "enter A" }, Log);
    }

    [Test]
    public void EnterByTypeRejectsPayloadStates()
    {
      _machine.Register(new Payload());
      Assert.Throws<InvalidOperationException>(() => _machine.Enter(typeof(Payload)));
    }

    [Test]
    public void DisposeExitsCurrentAndCancelsPending()
    {
      var a = _machine.Register(new A { EnterGate = new UniTaskCompletionSource() });
      _machine.Register(new B());
      _machine.Enter<A>().Forget();
      UniTask pending = _machine.Enter<B>();

      _machine.Dispose();
      a.EnterGate.TrySetResult();

      Assert.AreEqual(UniTaskStatus.Canceled, pending.Status);
      Assert.IsNull(_machine.CurrentState);
      CollectionAssert.AreEqual(new[] { "enter A", "exit A" }, Log);
      Assert.Throws<ObjectDisposedException>(() => _machine.Enter<A>());
    }

    [Test]
    public void StateChangedIsRaised()
    {
      _machine.Register(new A());
      var changes = new List<string>();
      _machine.StateChanged += (from, to) => changes.Add($"{from?.GetType().Name ?? "-"}>{to?.GetType().Name ?? "-"}");

      _machine.Enter<A>().Forget();
      _machine.Exit().Forget();

      CollectionAssert.AreEqual(new[] { "->A", "A>-" }, changes);
    }

    [Test]
    public void FactoryCreatesStatesWithMachine()
    {
      WithMachine state = _machine.Register<WithMachine>();
      Assert.AreSame(_machine, state.Machine);

      var machine = new StateMachine("Factory", new DelegateStateFactory(type => Activator.CreateInstance(type)));
      Assert.IsNotNull(machine.Register<A>());
      machine.Dispose();
    }

    [Test]
    public void SequentialMachineRunsInOrderAndFinishes()
    {
      var sequence = new SequentialStateMachine("Sequence");
      sequence.Register(new A());
      sequence.Register(new B());
      int completed = 0;
      sequence.Completed += () => completed++;

      sequence.Next().Forget();
      sequence.Next().Forget();
      Assert.IsTrue(sequence.IsIn<B>());
      Assert.AreEqual(1, sequence.CurrentIndex);

      sequence.Next().Forget();
      Assert.IsTrue(sequence.IsFinished);
      Assert.IsNull(sequence.CurrentState);
      Assert.AreEqual(1, completed);

      LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("finished"));
      sequence.Next().Forget();
      Assert.IsNull(sequence.CurrentState, "A finished sequence does not restart by itself.");

      sequence.Restart().Forget();
      Assert.IsTrue(sequence.IsIn<A>());
      Assert.IsFalse(sequence.IsFinished);
      sequence.Dispose();
    }

    [Test]
    public void TickReachesUpdatableState()
    {
      var state = new Ticking();
      _machine.Register(state);
      _machine.Enter<Ticking>().Forget();
      _machine.Tick(0.5f);
      Assert.AreEqual(0.5f, state.Total);
    }

    private sealed class Ticking : State, IUpdatableState
    {
      public float Total;
      public void Tick(float deltaTime) => Total += deltaTime;
    }
  }
}
