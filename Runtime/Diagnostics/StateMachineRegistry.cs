using System;
using System.Collections.Generic;
using UnityEngine;

namespace ExtState
{
  /// <summary>Weak list of living state machines, used by the debugger window.</summary>
  public static class StateMachineRegistry
  {
    private static readonly List<WeakReference<StateMachine>> Machines = new();

    public static event Action Changed;

    /// <summary>Fills <paramref name="results"/> with machines that are alive and not disposed.</summary>
    public static void GetMachines(List<StateMachine> results)
    {
      results.Clear();
      for (int i = Machines.Count - 1; i >= 0; i--)
      {
        if (Machines[i].TryGetTarget(out StateMachine machine) && !machine.IsDisposed)
          results.Add(machine);
        else
          Machines.RemoveAt(i);
      }

      results.Reverse();
    }

    internal static void Register(StateMachine machine)
    {
      Machines.Add(new WeakReference<StateMachine>(machine));
      Changed?.Invoke();
    }

    internal static void Unregister(StateMachine machine)
    {
      Machines.RemoveAll(reference => !reference.TryGetTarget(out StateMachine target) || target == machine);
      Changed?.Invoke();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
      Machines.Clear();
      Changed = null;
    }
  }
}
